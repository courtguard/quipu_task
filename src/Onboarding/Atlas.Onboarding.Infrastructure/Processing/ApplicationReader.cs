using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Shared.Auditing;
using Atlas.Shared.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Processing;

/// <summary>What the applicant is allowed to know about an application.</summary>
/// <param name="Id">The application's unique identifier.</param>
/// <param name="Market">The application's market.</param>
/// <param name="Status">The internal status.</param>
/// <param name="RejectionReason">Why it was rejected, if it was.</param>
public sealed record ApplicationSnapshot(Guid Id, string Market, ApplicationStatus Status,
    string? RejectionReason)
{
    /// <summary>The status as shown to the applicant.</summary>
    public string PublicStatus => PublicApplicationStatus.From(Status);
}

/// <summary>Reads application status, and waits for one to settle.</summary>
public sealed class ApplicationReader
{
    private readonly MarketDatabases _Databases;
    private readonly ProcessingOptions _Options;

    /// <summary>Creates the reader.</summary>
    public ApplicationReader(MarketDatabases databases, IOptions<ProcessingOptions> options)
    {
        _Databases = databases;
        _Options = options.Value;
    }

    /// <summary>Reads an application's current status.</summary>
    /// <param name="market">The application's market.</param>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when there is no such application.</returns>
    public async Task<ApplicationSnapshot?> FindAsync(MarketProfile market, Guid id,
        CancellationToken cancellationToken)
    {
        await using var context = _Databases.Create(market);
        return await context.Applications.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new ApplicationSnapshot(a.Id, a.Market, a.Status, a.RejectionReason))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Records that the applicant read their application's status.</summary>
    /// <param name="market">The application's market.</param>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task RecordStatusReadAsync(MarketProfile market, Guid id,
        CancellationToken cancellationToken)
    {
        await using var context = _Databases.Create(market);
        context.AccessLog.Add(new AccessLogEntry(id, AccessActors.APPLICANT,
            AccessActions.STATUS_READ, DateTime.UtcNow));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Waits until the application is no longer being worked on by the system, which means it is
    /// final or waiting for a person, or until the time runs out.
    /// </summary>
    /// <param name="market">The application's market.</param>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="timeout">The longest to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The latest snapshot, or null when there is no such application.</returns>
    public async Task<ApplicationSnapshot?> WaitForOutcomeAsync(MarketProfile market, Guid id,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var poll = TimeSpan.FromMilliseconds(_Options.WaitPollMilliseconds);
        while (true)
        {
            var snapshot = await FindAsync(market, id, cancellationToken);
            if (snapshot is null || !IsBeingWorkedOn(snapshot.Status)
                || DateTime.UtcNow >= deadline)
            {
                return snapshot;
            }

            await Task.Delay(poll, cancellationToken);
        }
    }

    private static bool IsBeingWorkedOn(ApplicationStatus status)
    {
        return status is ApplicationStatus.Processing or ApplicationStatus.Fulfilling;
    }
}
