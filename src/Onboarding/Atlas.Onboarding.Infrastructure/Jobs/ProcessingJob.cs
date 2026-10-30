namespace Atlas.Onboarding.Infrastructure.Jobs;

/// <summary>
/// The note that an application has work waiting. There is at most one per application, and it
/// exists only while there is something to do: a finished or parked application has none.
/// </summary>
public sealed class ProcessingJob
{
    private ProcessingJob()
    {
    }

    /// <summary>Queues the application for processing.</summary>
    /// <param name="applicationId">The application to work on.</param>
    /// <param name="now">The current time, in UTC; the work is due immediately.</param>
    public ProcessingJob(Guid applicationId, DateTime now)
    {
        ApplicationId = applicationId;
        NextAttemptAtUtc = now;
    }

    /// <summary>The application to work on.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>When the work is next due, in UTC.</summary>
    public DateTime NextAttemptAtUtc { get; private set; }

    /// <summary>How many times processing has had to be retried.</summary>
    public int Attempts { get; private set; }

    /// <summary>Until when a worker holds the job, in UTC. A crashed worker's hold ends.</summary>
    public DateTime? LockedUntilUtc { get; private set; }

    /// <summary>The worker holding the job.</summary>
    public string? LockedBy { get; private set; }

    /// <summary>The kind of the last failure; never personal data.</summary>
    public string? LastError { get; private set; }
}
