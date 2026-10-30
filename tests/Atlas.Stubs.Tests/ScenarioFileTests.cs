using System.Diagnostics;
using System.Net;
using Atlas.Stubs.Tests.Support;

namespace Atlas.Stubs.Tests;

/// <summary>
/// Proves the point of the design: behaviour changes by editing data, not code.
/// </summary>
public sealed class ScenarioFileTests : IDisposable
{
    private readonly string _File = Path.Combine(Path.GetTempPath(),
        $"scenarios-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_File);
    }

    private const string ONE_SLOW_RULE = """
        {
          "idNow": { "default": { "documentResult": "VALID", "faceMatch": true, "confidence": 0.9 },
            "rules": [ { "match": "tiny-delay", "delayMs": 250 } ] },
          "worldCheck": { "default": { "status": "CLEAR", "matches": [] }, "rules": [] },
          "accounts": { "default": {}, "rules": [] },
          "cards": { "default": {}, "rules": [] }
        }
        """;

    [Fact]
    public async Task A_rule_can_delay_the_answer()
    {
        File.WriteAllText(_File, ONE_SLOW_RULE);
        using var stubs = new StubClient(_File);

        var watch = Stopwatch.StartNew();
        var response = await stubs.IdentifyAsync("tiny-delay");
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(watch.ElapsedMilliseconds >= 200, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Editing_the_file_changes_behaviour_without_a_restart()
    {
        File.WriteAllText(_File, ONE_SLOW_RULE);
        using var stubs = new StubClient(_File);
        var before = await StubClient.BodyAsync(await stubs.IdentifyAsync("a new marker"));
        Assert.Equal("VALID", (string?)before["documentResult"]);

        File.WriteAllText(_File, ONE_SLOW_RULE.Replace("tiny-delay", "a new marker")
            .Replace("\"delayMs\": 250", "\"failStatus\": 500"));
        File.SetLastWriteTimeUtc(_File, DateTime.UtcNow.AddSeconds(5));

        var after = await stubs.IdentifyAsync("a new marker");

        Assert.Equal(HttpStatusCode.InternalServerError, after.StatusCode);
    }

    [Fact]
    public async Task A_broken_edit_keeps_the_last_good_behaviour()
    {
        File.WriteAllText(_File, ONE_SLOW_RULE);
        using var stubs = new StubClient(_File);
        await stubs.IdentifyAsync("anything");

        File.WriteAllText(_File, "{ this is not json");
        File.SetLastWriteTimeUtc(_File, DateTime.UtcNow.AddSeconds(5));

        var response = await stubs.IdentifyAsync("anything");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Refuses_to_start_when_the_file_is_broken()
    {
        File.WriteAllText(_File, "{ this is not json");

        Assert.ThrowsAny<Exception>(() => new StubClient(_File));
    }

    [Fact]
    public async Task The_shipped_scenarios_load_and_cover_every_documented_marker()
    {
        using var stubs = new StubClient();

        var response = await stubs.Http.GetAsync("/admin/scenarios");
        var body = await StubClient.BodyAsync(response);

        var idNow = body["idNow"]!["rules"]!.AsArray().Select(r => (string?)r!["match"]).ToList();
        Assert.Contains("scenario:inconclusive", idNow);
        Assert.Contains("scenario:down", idNow);
        Assert.Contains("scenario:flaky", idNow);
        var cards = body["cards"]!["rules"]!.AsArray().Select(r => (string?)r!["match"]).ToList();
        Assert.Contains("Card-Down", cards);
    }
}
