using System.Text.RegularExpressions;

namespace Atlas.Onboarding.Domain.Applications;

/// <summary>An application identifier taken apart.</summary>
/// <param name="Market">The market code the application belongs to.</param>
/// <param name="Id">The application's unique identifier.</param>
public readonly record struct ParsedApplicationId(string Market, Guid Id);

/// <summary>
/// The identifiers of applications as the outside world sees them. The market is part of the
/// identifier so that a request can be routed to the right country's database without a global
/// index of applicants, which would itself be personal data held outside the country.
/// </summary>
public static partial class ApplicationIds
{
    /// <summary>Builds the public identifier, for example <c>MB-3f2a...</c>.</summary>
    /// <param name="market">The market code.</param>
    /// <param name="id">The application's unique identifier.</param>
    /// <returns>The public identifier.</returns>
    public static string Format(string market, Guid id)
    {
        return $"{market.ToUpperInvariant()}-{id:N}";
    }

    /// <summary>Reads a public identifier.</summary>
    /// <param name="value">The text to read. May be null.</param>
    /// <returns>The parts, or null when the text is not a well-formed identifier.</returns>
    public static ParsedApplicationId? Parse(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = Pattern().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var id = Guid.ParseExact(match.Groups["id"].Value, "N");
        return new ParsedApplicationId(match.Groups["market"].Value.ToUpperInvariant(), id);
    }

    [GeneratedRegex("^(?<market>[A-Za-z]{2})-(?<id>[0-9a-fA-F]{32})$",
        RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Pattern();
}
