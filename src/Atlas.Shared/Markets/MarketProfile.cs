namespace Atlas.Shared.Markets;

/// <summary>
/// Everything that differs between the markets Atlas operates in. A market is configuration,
/// not code: adding or changing one must not require a change to the workflow.
/// </summary>
public sealed class MarketProfile
{
    /// <summary>The internal market code, for example "MB", taken from the config key.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Whether applications are accepted. A market must stay disabled until its Annex B entry has
    /// been reissued by Group Compliance.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>How an approved application is turned into an active account.</summary>
    public ActivationMode ActivationMode { get; set; } = ActivationMode.Remote;

    /// <summary>The market's local identifier rules.</summary>
    public IdentifierRules Identifier { get; set; } = new();

    /// <summary>
    /// The minimum document and face-match confidence from IDNow, from 0 to 1, below which the
    /// identity is not considered verified.
    /// </summary>
    public double IdNowMinConfidence { get; set; }

    /// <summary>
    /// The database holding this market's customer records. Personal data must stay in the
    /// customer's country, so every market gets its own.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The blob container holding this market's identity documents and selfies.</summary>
    public string BlobContainer { get; set; } = string.Empty;

    /// <summary>Base address of the IDNow endpoint to use for this market.</summary>
    public string IdNowBaseUrl { get; set; } = string.Empty;

    /// <summary>Base address of the World-Check endpoint to use for this market.</summary>
    public string WorldCheckBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base address of the core banking endpoint that opens accounts and orders cards for this
    /// market.
    /// </summary>
    public string CoreBankingBaseUrl { get; set; } = string.Empty;
}
