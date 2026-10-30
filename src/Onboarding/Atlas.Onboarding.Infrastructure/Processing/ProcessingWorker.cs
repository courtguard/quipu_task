using Atlas.Onboarding.Infrastructure.Jobs;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Shared.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Processing;

/// <summary>
/// Takes due jobs from every market's queue and runs them through the processor. Every replica
/// runs one; the queue's claim makes sure no two take the same job.
/// </summary>
public sealed class ProcessingWorker : BackgroundService
{
    private readonly MarketDatabases _Databases;
    private readonly MarketRegistry _Markets;
    private readonly ApplicationProcessor _Processor;
    private readonly ProcessingSignal _Signal;
    private readonly TimeProvider _Time;
    private readonly ProcessingOptions _Options;
    private readonly ILogger<ProcessingWorker> _Logger;
    private readonly string _WorkerName = WorkerName();

    /// <summary>Creates the worker.</summary>
    public ProcessingWorker(MarketDatabases databases, MarketRegistry markets,
        ApplicationProcessor processor, ProcessingSignal signal, TimeProvider time,
        IOptions<ProcessingOptions> options, ILogger<ProcessingWorker> logger)
    {
        _Databases = databases;
        _Markets = markets;
        _Processor = processor;
        _Signal = signal;
        _Time = time;
        _Options = options.Value;
        _Logger = logger;
    }

    /// <summary>Runs one pass over every enabled market, as the polling loop does.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many jobs were worked on.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var worked = 0;
        foreach (var market in _Markets.All.Where(m => m.Enabled))
        {
            worked += await RunMarketAsync(market, cancellationToken);
        }

        return worked;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(_Options.WorkerPollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(stoppingToken) == 0)
                {
                    await _Signal.WaitAsync(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database is unreachable. Keep going: it will come back.
                _Logger.LogError("The worker could not poll: {Kind}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    // Names the worker in the job table, so that a stuck hold can be traced to a replica.
    private static string WorkerName()
    {
        var machine = Environment.MachineName;
        return $"{machine[..Math.Min(15, machine.Length)]}:{Guid.NewGuid():N}";
    }

    private async Task<int> RunMarketAsync(MarketProfile market,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedJob> jobs;
        await using (var context = _Databases.Create(market))
        {
            jobs = await JobQueue.ClaimDueAsync(context, _WorkerName,
                _Time.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(_Options.LeaseSeconds),
                _Options.WorkerConcurrency, cancellationToken);
        }

        await Task.WhenAll(jobs.Select(job => RunJobAsync(market, job, cancellationToken)));
        return jobs.Count;
    }

    private async Task RunJobAsync(MarketProfile market, ClaimedJob job,
        CancellationToken cancellationToken)
    {
        var outcome = ProcessingOutcome.RetryLater;
        var error = "retry";
        try
        {
            outcome = await _Processor.ProcessAsync(market.Code, job.ApplicationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the job's hold runs out and another replica takes it over.
            return;
        }
        catch (Exception ex)
        {
            // Never log the exception's message: it may contain personal data.
            error = ex.GetType().Name;
            _Logger.LogError("Application {ApplicationId} failed: {Kind}", job.ApplicationId,
                error);
        }

        await using var context = _Databases.Create(market);
        if (outcome == ProcessingOutcome.Finished)
        {
            await JobQueue.CompleteAsync(context, job.ApplicationId, cancellationToken);
            return;
        }

        var attempts = job.Attempts + 1;
        var next = _Time.GetUtcNow().UtcDateTime + _Options.RetryDelay(job.Attempts);
        await JobQueue.RescheduleAsync(context, job.ApplicationId, attempts, next, error,
            cancellationToken);
    }
}
