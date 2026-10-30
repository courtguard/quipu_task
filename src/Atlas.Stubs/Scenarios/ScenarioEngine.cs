using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Atlas.Stubs.Scenarios;

/// <summary>
/// Chooses what a stood-in service does with a call, from the scenario rules.
/// </summary>
public sealed class ScenarioEngine
{
    private readonly ConcurrentDictionary<string, int> _Calls = new();

    /// <summary>
    /// Finds the rule for a call, waits if the rule says so, and decides between failing and
    /// answering.
    /// </summary>
    /// <param name="set">The behaviours of the service being called.</param>
    /// <param name="serviceName">Names the service, to keep call counts apart.</param>
    /// <param name="triggerInput">The text searched for markers.</param>
    /// <param name="callKey">
    /// Identifies the logical request, so that "fail the first two calls" counts retries of the
    /// same request and not every caller together.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The failure status or a copy of the answer to give.</returns>
    public async Task<ScenarioOutcome> RunAsync(ScenarioSet set, string serviceName,
        string triggerInput, string callKey, CancellationToken cancellationToken)
    {
        var rule = set.Rules.FirstOrDefault(r =>
            r.Match.Length > 0
            && triggerInput.Contains(r.Match, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            return new ScenarioOutcome(null, Copy(set.Default));
        }

        if (rule.DelayMs > 0)
        {
            await Task.Delay(rule.DelayMs, cancellationToken);
        }

        if (rule.FailStatus is { } status && ShouldFail(rule, serviceName, callKey))
        {
            return new ScenarioOutcome(status, new JsonObject());
        }

        return new ScenarioOutcome(null, Copy(rule.Response ?? set.Default));
    }

    /// <summary>Forgets every call count, so that "fail the first N" starts over.</summary>
    public void Reset() => _Calls.Clear();

    /// <summary>A short, stable identifier derived from a value, for provider-style ids.</summary>
    /// <param name="value">The value to derive it from.</param>
    /// <returns>Eight lowercase hexadecimal characters.</returns>
    public static string ShortId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private bool ShouldFail(ScenarioRule rule, string serviceName, string callKey)
    {
        if (rule.FailFirst <= 0)
        {
            return true;
        }

        var counter = $"{serviceName}|{rule.Match}|{callKey}";
        var calls = _Calls.AddOrUpdate(counter, 1, (_, previous) => previous + 1);
        return calls <= rule.FailFirst;
    }

    private static JsonObject Copy(JsonObject source) => (JsonObject)source.DeepClone();
}
