namespace Atlas.Onboarding.Domain.Identity;

/// <summary>
/// A named check-digit algorithm for a market's national identifier. Markets refer to an
/// algorithm by <see cref="Name" /> in configuration.
/// </summary>
public interface IChecksumAlgorithm
{
    /// <summary>The name used to select this algorithm in a market's configuration.</summary>
    string Name { get; }

    /// <summary>Checks the check digit of an identifier that already matches its format.</summary>
    /// <param name="value">The identifier, trimmed.</param>
    /// <returns>True when the check digit is correct.</returns>
    bool IsValid(string value);
}
