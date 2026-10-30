using Atlas.Shared.Applications;
using Atlas.Shared.Auditing;
using Atlas.Shared.Markets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Review.Api.Data;

/// <summary>How the Review API reaches SQL Server.</summary>
public sealed class SqlOptions
{
    /// <summary>
    /// Connection string without a database, using the Review API's own login. The database is
    /// chosen per market.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>The outcome of recording a decision.</summary>
public enum RecordOutcome
{
    /// <summary>Recorded; the onboarding service will apply it.</summary>
    Recorded,

    /// <summary>The case is not waiting for this kind of decision.</summary>
    NotPending,

    /// <summary>A decision of this kind was already recorded.</summary>
    AlreadyDecided
}

/// <summary>
/// Reads cases and records decisions in a market's database. Every look at a case's personal
/// data is written to the access log, with the name of the person who looked.
/// </summary>
public sealed class StaffCases
{
    /// <summary>The stage of a case waiting for a compliance officer.</summary>
    public const string REVIEW = "REVIEW";

    /// <summary>The stage of a case waiting for a branch visit.</summary>
    public const string BRANCH = "BRANCH";

    private readonly string _BaseConnectionString;
    private readonly TimeProvider _Time;

    /// <summary>Creates the service.</summary>
    public StaffCases(IOptions<SqlOptions> options, TimeProvider time)
    {
        _BaseConnectionString = options.Value.ConnectionString;
        _Time = time;
    }

    /// <summary>Lists the cases waiting at a stage, without personal data.</summary>
    /// <param name="market">The market.</param>
    /// <param name="stage">REVIEW or BRANCH.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The waiting cases, oldest first.</returns>
    public async Task<IReadOnlyList<ReviewCase>> ListAsync(MarketProfile market, string stage,
        CancellationToken cancellationToken)
    {
        await using var context = Create(market);
        return await context.Cases.AsNoTracking()
            .Where(c => c.Stage == stage)
            .OrderBy(c => c.SubmittedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads one waiting case and records who looked at it.</summary>
    /// <param name="market">The market.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="stage">REVIEW or BRANCH.</param>
    /// <param name="actor">The person looking, as named in the access log.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The case, or null when no such case is waiting at that stage.</returns>
    public async Task<ReviewCase?> ViewAsync(MarketProfile market, Guid applicationId,
        string stage, string actor, CancellationToken cancellationToken)
    {
        await using var context = Create(market);
        var found = await FindAsync(context, applicationId, stage, cancellationToken);
        if (found is null)
        {
            return null;
        }

        context.AccessLog.Add(Access(applicationId, actor, AccessActions.CASE_VIEWED));
        await context.SaveChangesAsync(cancellationToken);
        return found;
    }

    /// <summary>
    /// Records a decision and queues the application, so that the onboarding service applies
    /// it. The decision, the queue entry and the access log entry are written together.
    /// </summary>
    /// <param name="market">The market.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="stage">The stage the decision belongs to.</param>
    /// <param name="kind">What was decided.</param>
    /// <param name="actor">Who decided, as named in the access log.</param>
    /// <param name="note">An optional note.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened.</returns>
    public async Task<RecordOutcome> RecordAsync(MarketProfile market, Guid applicationId,
        string stage, StaffDecisionKind kind, string actor, string? note,
        CancellationToken cancellationToken)
    {
        await using var context = Create(market);
        if (await FindAsync(context, applicationId, stage, cancellationToken) is null)
        {
            return RecordOutcome.NotPending;
        }

        var now = _Time.GetUtcNow().UtcDateTime;
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            context.Decisions.Add(new DecisionRecord
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                Kind = kind,
                DecidedBy = actor,
                Note = note,
                DecidedAtUtc = now
            });
            context.AccessLog.Add(Access(applicationId, actor, AccessActions.DECISION_RECORDED));
            await context.SaveChangesAsync(cancellationToken);

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.Jobs (ApplicationId, NextAttemptAtUtc, Attempts)
                SELECT {applicationId}, {now}, 0
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Jobs WHERE ApplicationId = {applicationId})
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RecordOutcome.Recorded;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException
                                           {
                                               Number: 2601 or 2627
                                           })
        {
            return RecordOutcome.AlreadyDecided;
        }
    }

    private static Task<ReviewCase?> FindAsync(ReviewDbContext context, Guid applicationId,
        string stage, CancellationToken cancellationToken)
    {
        return context.Cases.AsNoTracking()
            .Where(c => c.ApplicationId == applicationId && c.Stage == stage)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private AccessRecord Access(Guid applicationId, string actor, string action)
    {
        return new AccessRecord
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Actor = actor,
            Action = action,
            OccurredAtUtc = _Time.GetUtcNow().UtcDateTime
        };
    }

    private ReviewDbContext Create(MarketProfile market)
    {
        var connection = new SqlConnectionStringBuilder(_BaseConnectionString)
        {
            InitialCatalog = market.Database
        }.ConnectionString;
        return new ReviewDbContext(new DbContextOptionsBuilder<ReviewDbContext>()
            .UseSqlServer(connection).Options);
    }
}
