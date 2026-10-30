namespace Atlas.Onboarding.Infrastructure.Providers;

/// <summary>
/// The outcome of calling an outside provider: either its answer, or the fact that no usable
/// answer arrived. Not getting an answer says nothing about the applicant, so it is kept apart
/// from every business outcome.
/// </summary>
/// <typeparam name="T">The kind of answer.</typeparam>
public sealed class ProviderResult<T>
{
    private readonly T? _Value;

    private ProviderResult(T? value, string? reason)
    {
        _Value = value;
        Reason = reason;
    }

    /// <summary>Whether the provider gave a usable answer.</summary>
    public bool IsSuccess => Reason is null;

    /// <summary>Why no answer was obtained, when none was. Never contains personal data.</summary>
    public string? Reason { get; }

    /// <summary>The provider's answer.</summary>
    /// <exception cref="InvalidOperationException">There is no answer.</exception>
    public T Value => IsSuccess
        ? _Value!
        : throw new InvalidOperationException("The provider gave no answer: " + Reason);

    /// <summary>A usable answer.</summary>
    /// <param name="value">The answer.</param>
    /// <returns>The result.</returns>
    public static ProviderResult<T> Success(T value) => new(value, null);

    /// <summary>No usable answer: the provider was slow, down, or answered nonsense.</summary>
    /// <param name="reason">A short description without personal data.</param>
    /// <returns>The result.</returns>
    public static ProviderResult<T> Unavailable(string reason) => new(default, reason);
}
