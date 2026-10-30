namespace Atlas.Onboarding.Infrastructure.Processing;

/// <summary>
/// Wakes the worker in the same process as soon as an application is queued, so that a new
/// application does not wait for the next poll. It is only a shortcut: a worker in another
/// process finds the job by polling.
/// </summary>
public sealed class ProcessingSignal
{
    private readonly SemaphoreSlim _Semaphore = new(0, 1);

    /// <summary>Tells the worker that work has arrived.</summary>
    public void Notify()
    {
        try
        {
            _Semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; one wake-up covers every job queued meanwhile.
        }
    }

    /// <summary>Waits until work arrives or the time is up.</summary>
    /// <param name="timeout">The longest to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _Semaphore.WaitAsync(timeout, cancellationToken);
    }
}
