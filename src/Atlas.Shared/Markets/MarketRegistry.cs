using System.Text.RegularExpressions;

namespace Atlas.Shared.Markets;

/// <summary>
/// The validated set of markets loaded from configuration.
/// </summary>
public sealed class MarketRegistry
{
    private static readonly TimeSpan _RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<string, MarketProfile> _Markets;

    /// <summary>
    /// Builds the registry and fails fast if any market is misconfigured, so that a typo is found
    /// at start-up and not when the first customer from that market applies.
    /// </summary>
    /// <param name="markets">The configured markets keyed by market code. Must not be null.</param>
    /// <exception cref="InvalidOperationException">
    /// No market is configured, or a market's configuration is invalid.
    /// </exception>
    public MarketRegistry(IReadOnlyDictionary<string, MarketProfile> markets)
    {
        ArgumentNullException.ThrowIfNull(markets);

        _Markets = new Dictionary<string, MarketProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, profile) in markets)
        {
            profile.Code = code.ToUpperInvariant();
            NormalizeOptionalSettings(profile.Identifier);
            _Markets[profile.Code] = profile;
        }

        var problems = Validate(_Markets.Values).ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid market configuration: " + string.Join("; ", problems));
        }
    }

    /// <summary>All configured markets, enabled or not.</summary>
    public IReadOnlyCollection<MarketProfile> All => _Markets.Values;

    /// <summary>Finds a market by its code, ignoring case.</summary>
    /// <param name="code">The market code, for example "MB". May be null.</param>
    /// <returns>The market, or null when the code is unknown.</returns>
    public MarketProfile? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return _Markets.GetValueOrDefault(code.Trim());
    }

    // The JSON configuration provider turns an explicit null into an empty string, so a blank
    // optional setting has to be read as "not set".
    private static void NormalizeOptionalSettings(IdentifierRules rules)
    {
        rules.NationalIdPattern = NullIfBlank(rules.NationalIdPattern);
        rules.PassportPattern = NullIfBlank(rules.PassportPattern);
        rules.Checksum = NullIfBlank(rules.Checksum);
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IEnumerable<string> Validate(IEnumerable<MarketProfile> markets)
    {
        var any = false;
        foreach (var market in markets)
        {
            any = true;
            foreach (var problem in ValidateMarket(market))
            {
                yield return $"{market.Code}: {problem}";
            }
        }

        if (!any)
        {
            yield return "no markets are configured";
        }
    }

    private static IEnumerable<string> ValidateMarket(MarketProfile market)
    {
        if (market.IdNowMinConfidence is < 0 or > 1)
        {
            yield return "IdNowMinConfidence must be between 0 and 1";
        }

        if (string.IsNullOrWhiteSpace(market.Database))
        {
            yield return "Database is required";
        }

        if (string.IsNullOrWhiteSpace(market.BlobContainer))
        {
            yield return "BlobContainer is required";
        }

        if (!Uri.IsWellFormedUriString(market.IdNowBaseUrl, UriKind.Absolute))
        {
            yield return "IdNowBaseUrl must be an absolute URL";
        }

        if (!Uri.IsWellFormedUriString(market.WorldCheckBaseUrl, UriKind.Absolute))
        {
            yield return "WorldCheckBaseUrl must be an absolute URL";
        }

        if (!Uri.IsWellFormedUriString(market.CoreBankingBaseUrl, UriKind.Absolute))
        {
            yield return "CoreBankingBaseUrl must be an absolute URL";
        }

        if (market.Identifier.PassportAcceptedInLieu && market.Identifier.PassportPattern is null)
        {
            yield return "PassportPattern is required when passports are accepted in lieu";
        }

        foreach (var pattern in new[]
                 {
                     market.Identifier.NationalIdPattern,
                     market.Identifier.PassportPattern
                 })
        {
            if (pattern is not null && !IsValidRegex(pattern))
            {
                yield return $"'{pattern}' is not a valid regular expression";
            }
        }
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase, _RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
