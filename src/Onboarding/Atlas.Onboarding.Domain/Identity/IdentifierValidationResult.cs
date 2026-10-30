namespace Atlas.Onboarding.Domain.Identity;

/// <summary>
/// The outcome of checking an applicant's identifiers against their market's rules.
/// </summary>
public sealed class IdentifierValidationResult
{
    private IdentifierValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    /// <summary>Whether the identifiers satisfy the market's rules.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Stable error codes for the mobile app, empty when valid. The offending value itself is
    /// never included, because it is personal data.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>A result with no errors.</summary>
    public static IdentifierValidationResult Valid { get; } = new(Array.Empty<string>());

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error code.</param>
    /// <returns>The failed result.</returns>
    public static IdentifierValidationResult Invalid(string error) => new(new[] { error });
}
