using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Shared.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Retention;

/// <summary>How often records past their retention period are deleted.</summary>
public sealed class RetentionOptions
{
    /// <summary>Minutes between runs.</summary>
    public double IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// The target records per market and run. Records tied at the cutoff are deleted together.
    /// </summary>
    public int BatchSize { get; set; } = 500;
}

/// <summary>
/// Deletes everything about applications whose retention period has ended: the images, then
/// the application, its documents, decisions, queued work and access log, in one transaction.
/// The rows are deleted by a database procedure that only removes records past their retention
/// date, so the service itself has no right to delete access log rows as it pleases.
/// </summary>
public sealed class RetentionPurger
{
    private readonly MarketDatabases _Databases;
    private readonly IDocumentStore _Documents;
    private readonly RetentionOptions _Options;
    private readonly ILogger<RetentionPurger> _Logger;

    /// <summary>Creates the purger.</summary>
    public RetentionPurger(MarketDatabases databases, IDocumentStore documents,
        IOptions<RetentionOptions> options, ILogger<RetentionPurger> logger)
    {
        _Databases = databases;
        _Documents = documents;
        _Options = options.Value;
        _Logger = logger;
    }

    /// <summary>Deletes the records of one market whose retention ended by a given time.</summary>
    /// <param name="market">The market.</param>
    /// <param name="cutoffUtc">Records retained until this time or earlier are deleted.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>How many applications were deleted.</returns>
    public async Task<int> PurgeAsync(MarketProfile market, DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var context = _Databases.Create(market);
        var due = await context.Applications.AsNoTracking()
            .Where(a => a.RetentionUntilUtc <= cutoffUtc)
            .OrderBy(a => a.RetentionUntilUtc)
            .Take(_Options.BatchSize)
            .Select(a => a.RetentionUntilUtc)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        // Images first: if this run stops half-way, the rows are still there and the next run
        // finds the same records again. Include every record the cutoff-based procedure can
        // delete, including records tied at the batch boundary, so no blobs are orphaned.
        var batchCutoff = due.Max();
        var blobs = await (
                from document in context.Documents.AsNoTracking()
                join application in context.Applications.AsNoTracking()
                    on document.ApplicationId equals application.Id
                where application.RetentionUntilUtc <= batchCutoff
                select document.BlobName)
            .ToListAsync(cancellationToken);
        foreach (var blob in blobs)
        {
            await _Documents.DeleteAsync(market, blob, cancellationToken);
        }

        var count = await ExecutePurgeAsync(context, batchCutoff, cancellationToken);
        _Logger.LogInformation("Deleted {Count} applications in {Market} past their retention",
            count, market.Code);
        return count;
    }

    private static async Task<int> ExecutePurgeAsync(OnboardingDbContext context,
        DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "dbo.PurgeExpiredApplications";
            command.CommandType = System.Data.CommandType.StoredProcedure;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@cutoff";
            parameter.DbType = System.Data.DbType.DateTime2;
            parameter.Value = cutoffUtc;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}

/// <summary>Runs the retention purge for every market at an interval.</summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly RetentionPurger _Purger;
    private readonly MarketRegistry _Markets;
    private readonly TimeProvider _Time;
    private readonly RetentionOptions _Options;
    private readonly ILogger<RetentionWorker> _Logger;

    /// <summary>Creates the worker.</summary>
    public RetentionWorker(RetentionPurger purger, MarketRegistry markets, TimeProvider time,
        IOptions<RetentionOptions> options, ILogger<RetentionWorker> logger)
    {
        _Purger = purger;
        _Markets = markets;
        _Time = time;
        _Options = options.Value;
        _Logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_Options.IntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var market in _Markets.All.Where(m => m.Enabled))
            {
                try
                {
                    await _Purger.PurgeAsync(market, _Time.GetUtcNow().UtcDateTime,
                        stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _Logger.LogError("Retention purge failed for {Market}: {Kind}", market.Code,
                        ex.GetType().Name);
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
