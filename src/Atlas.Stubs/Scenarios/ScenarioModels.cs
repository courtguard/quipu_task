using System.Text.Json.Nodes;

namespace Atlas.Stubs.Scenarios;

/// <summary>
/// One behaviour the stand-in can be asked to show, selected when its marker appears in the
/// request.
/// </summary>
public sealed class ScenarioRule
{
    /// <summary>
    /// Text that selects this rule when it appears anywhere in the request's trigger input,
    /// ignoring case.
    /// </summary>
    public string Match { get; set; } = string.Empty;

    /// <summary>How long to wait before answering, to simulate a slow provider.</summary>
    public int DelayMs { get; set; }

    /// <summary>
    /// HTTP status to fail with, to simulate an outage. Combined with <see cref="FailFirst" />
    /// the failure is only temporary.
    /// </summary>
    public int? FailStatus { get; set; }

    /// <summary>
    /// When above zero, only the first this-many calls for the same request fail and later ones
    /// succeed. Zero means that every call fails.
    /// </summary>
    public int FailFirst { get; set; }

    /// <summary>The answer to give when the call succeeds; the set's default when omitted.</summary>
    public JsonObject? Response { get; set; }
}

/// <summary>The behaviours of one stood-in service.</summary>
public sealed class ScenarioSet
{
    /// <summary>The normal, happy-path answer.</summary>
    public JsonObject Default { get; set; } = new();

    /// <summary>Rules checked in order; the first whose marker matches wins.</summary>
    public List<ScenarioRule> Rules { get; set; } = new();
}

/// <summary>The contents of the scenarios file.</summary>
public sealed class ScenarioFile
{
    /// <summary>Behaviours of the identity verification provider.</summary>
    public ScenarioSet IdNow { get; set; } = new();

    /// <summary>Behaviours of the sanctions screening provider.</summary>
    public ScenarioSet WorldCheck { get; set; } = new();

    /// <summary>Behaviours of core banking's account opening.</summary>
    public ScenarioSet Accounts { get; set; } = new();

    /// <summary>Behaviours of core banking's card ordering.</summary>
    public ScenarioSet Cards { get; set; } = new();
}

/// <summary>What a stood-in service should do with one call.</summary>
/// <param name="FailStatus">The HTTP status to fail with, or null to succeed.</param>
/// <param name="Body">The answer to give when succeeding.</param>
public sealed record ScenarioOutcome(int? FailStatus, JsonObject Body);
