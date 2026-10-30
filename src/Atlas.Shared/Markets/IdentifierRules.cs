namespace Atlas.Shared.Markets;

/// <summary>
/// The local identifier rules of one market (Annex B of the compliance position). The values
/// are used for validation only and never as a key for customer records.
/// </summary>
public sealed class IdentifierRules
{
    /// <summary>
    /// Whether the applicant must supply some identifier. When true and no national ID is given,
    /// a passport number is accepted instead only if <see cref="PassportAcceptedInLieu" /> is set.
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// Regular expression the market's national identifier must fully match (case-insensitive).
    /// When null, only a sanity length limit is applied.
    /// </summary>
    public string? NationalIdPattern { get; set; }

    /// <summary>
    /// Name of the check-digit algorithm for the national identifier, or null when the market has
    /// no algorithm that we are able to validate.
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Whether residents without a national identifier may supply a passport number instead.
    /// </summary>
    public bool PassportAcceptedInLieu { get; set; }

    /// <summary>
    /// Regular expression the passport number must fully match (case-insensitive). Only a sanity
    /// check, because passport number formats are not part of the market requirements.
    /// </summary>
    public string? PassportPattern { get; set; }
}
