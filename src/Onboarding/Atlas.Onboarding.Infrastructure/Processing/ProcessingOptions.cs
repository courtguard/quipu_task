namespace Atlas.Onboarding.Infrastructure.Processing;

/// <summary>Timings of application processing. All are configuration.</summary>
public sealed class ProcessingOptions
{
    /// <summary>
    /// How long <c>POST /applications</c> waits for a final answer before answering "accepted".
    /// Well under the 3-minute promise and under typical mobile HTTP timeouts.
    /// </summary>
    public int SyncWaitSeconds { get; set; } = 60;

    /// <summary>How often the waiting request looks at the application's status.</summary>
    public int WaitPollMilliseconds { get; set; } = 200;

    /// <summary>
    /// How long an application may stay unverified, for example because a provider is down,
    /// before it expires. Applications under compliance review never expire.
    /// </summary>
    public double MaxProcessingAgeHours { get; set; } = 24;

    /// <summary>How often an idle worker looks for due jobs.</summary>
    public int WorkerPollMilliseconds { get; set; } = 500;

    /// <summary>How many applications one replica works on at the same time.</summary>
    public int WorkerConcurrency { get; set; } = 8;

    /// <summary>
    /// How long a worker holds a job. If the worker crashes, another takes the job over once
    /// this has passed. It must exceed the longest a single attempt can take.
    /// </summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>The first retry delay after a failed attempt; each later one doubles.</summary>
    public double RetryBaseSeconds { get; set; } = 2;

    /// <summary>The longest delay between retries.</summary>
    public double RetryMaxSeconds { get; set; } = 300;

    /// <summary>The delay before retry number <paramref name="attempts" />.</summary>
    /// <param name="attempts">How many retries have already happened.</param>
    /// <returns>The delay.</returns>
    public TimeSpan RetryDelay(int attempts)
    {
        var seconds = RetryBaseSeconds * Math.Pow(2, Math.Min(attempts, 20));
        return TimeSpan.FromSeconds(Math.Min(seconds, RetryMaxSeconds));
    }
}
