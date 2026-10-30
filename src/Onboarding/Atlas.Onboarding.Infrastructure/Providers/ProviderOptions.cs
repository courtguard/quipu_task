namespace Atlas.Onboarding.Infrastructure.Providers;

/// <summary>How patiently the service deals with outside providers.</summary>
public sealed class ProviderOptions
{
    /// <summary>
    /// How long to wait for one call. A provider slower than this is treated as unavailable for
    /// this attempt; the work is retried later.
    /// </summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>The longest one logical call may take, retries included.</summary>
    public int TotalTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a failed call is repeated straight away; at least 1. Kept small, because
    /// longer outages are handled by the worker's retries.
    /// </summary>
    public int RetryAttempts { get; set; } = 2;

    /// <summary>The delay before the first immediate retry; later ones back off.</summary>
    public int RetryDelayMilliseconds { get; set; } = 200;
}
