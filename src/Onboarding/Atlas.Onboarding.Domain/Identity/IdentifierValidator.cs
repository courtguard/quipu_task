using System.Text.RegularExpressions;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Identity;

/// <summary>
/// Validates an applicant's national identifier or passport number against the rules of their
/// market. The rules are configuration (<see cref="IdentifierRules" />); only check-digit
/// algorithms are code.
/// </summary>
public sealed class IdentifierValidator
{
    /// <summary>Error: no usable identifier was supplied.</summary>
    public const string IDENTIFIER_REQUIRED = "IDENTIFIER_REQUIRED";

    /// <summary>Error: the national identifier does not have the market's format.</summary>
    public const string NATIONAL_ID_INVALID_FORMAT = "NATIONAL_ID_INVALID_FORMAT";

    /// <summary>Error: the national identifier's check digit is wrong.</summary>
    public const string NATIONAL_ID_INVALID_CHECKSUM = "NATIONAL_ID_INVALID_CHECKSUM";

    /// <summary>Error: the passport number does not look like a passport number.</summary>
    public const string PASSPORT_NUMBER_INVALID_FORMAT = "PASSPORT_NUMBER_INVALID_FORMAT";

    private const int MAX_IDENTIFIER_LENGTH = 32;
    private static readonly TimeSpan _RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<string, IChecksumAlgorithm> _Algorithms;

    /// <summary>Creates a validator that can use the supplied check-digit algorithms.</summary>
    /// <param name="algorithms">The available algorithms; may be empty.</param>
    public IdentifierValidator(IEnumerable<IChecksumAlgorithm> algorithms)
    {
        _Algorithms = algorithms.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists configuration problems that would only surface when a customer applies, namely a
    /// market naming a check-digit algorithm that does not exist.
    /// </summary>
    /// <param name="markets">The markets to check.</param>
    /// <returns>A description of each problem; empty when the configuration is usable.</returns>
    public IReadOnlyList<string> FindConfigurationProblems(IEnumerable<MarketProfile> markets)
    {
        return markets
            .Where(m => m.Identifier.Checksum is not null
                        && !_Algorithms.ContainsKey(m.Identifier.Checksum))
            .Select(m => $"{m.Code}: unknown checksum '{m.Identifier.Checksum}'")
            .ToList();
    }

    /// <summary>
    /// Checks the identifiers an applicant supplied. A national identifier, when given, is always
    /// validated; a passport number is only considered when the market accepts it in lieu of a
    /// national identifier and none was supplied.
    /// </summary>
    /// <param name="market">The applicant's market.</param>
    /// <param name="nationalId">The national identifier, or null or blank when absent.</param>
    /// <param name="passportNumber">The passport number, or null or blank when absent.</param>
    /// <returns>The outcome, with error codes when invalid.</returns>
    public IdentifierValidationResult Validate(MarketProfile market, string? nationalId,
        string? passportNumber)
    {
        ArgumentNullException.ThrowIfNull(market);
        var rules = market.Identifier;
        var id = nationalId?.Trim();
        var passport = passportNumber?.Trim();

        if (!string.IsNullOrEmpty(id))
        {
            return ValidateNationalId(rules, id);
        }

        if (rules.PassportAcceptedInLieu && !string.IsNullOrEmpty(passport))
        {
            return Matches(passport, rules.PassportPattern)
                ? IdentifierValidationResult.Valid
                : IdentifierValidationResult.Invalid(PASSPORT_NUMBER_INVALID_FORMAT);
        }

        return rules.Required
            ? IdentifierValidationResult.Invalid(IDENTIFIER_REQUIRED)
            : IdentifierValidationResult.Valid;
    }

    private IdentifierValidationResult ValidateNationalId(IdentifierRules rules, string id)
    {
        if (id.Length > MAX_IDENTIFIER_LENGTH || !Matches(id, rules.NationalIdPattern))
        {
            return IdentifierValidationResult.Invalid(NATIONAL_ID_INVALID_FORMAT);
        }

        if (rules.Checksum is not null)
        {
            var algorithm = _Algorithms.GetValueOrDefault(rules.Checksum);
            if (algorithm is null || !algorithm.IsValid(id))
            {
                return IdentifierValidationResult.Invalid(NATIONAL_ID_INVALID_CHECKSUM);
            }
        }

        return IdentifierValidationResult.Valid;
    }

    private static bool Matches(string value, string? pattern)
    {
        return pattern is null
               || Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, _RegexTimeout);
    }
}
