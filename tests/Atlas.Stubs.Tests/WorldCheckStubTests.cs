using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Atlas.Stubs.Tests.Support;

namespace Atlas.Stubs.Tests;

public sealed class WorldCheckStubTests : IDisposable
{
    private readonly StubClient _Stubs = new();

    public void Dispose() => _Stubs.Dispose();

    [Fact]
    public async Task An_ordinary_name_is_clear()
    {
        var response = await _Stubs.ScreenAsync("Ana Petrova");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await StubClient.BodyAsync(response);
        Assert.Equal("CLEAR", (string?)body["status"]);
        Assert.Empty(body["matches"]!.AsArray());
        Assert.StartsWith("wc_", (string?)body["caseId"]);
    }

    [Fact]
    public async Task A_sanctions_marker_in_the_name_gives_a_possible_match()
    {
        var response = await _Stubs.ScreenAsync("Ana Possible-Match");

        var body = await StubClient.BodyAsync(response);
        Assert.Equal("POSSIBLE_MATCH", (string?)body["status"]);
        var match = Assert.Single(body["matches"]!.AsArray());
        Assert.Equal("SANCTIONS", (string?)match!["listType"]);
    }

    [Fact]
    public async Task A_pep_marker_in_the_name_gives_a_pep_match()
    {
        var response = await _Stubs.ScreenAsync("Ana Pep-Match");

        var body = await StubClient.BodyAsync(response);
        var match = Assert.Single(body["matches"]!.AsArray());
        Assert.Equal("PEP", (string?)match!["listType"]);
    }

    [Fact]
    public async Task A_down_provider_answers_503_and_a_flaky_one_recovers()
    {
        var down = await _Stubs.ScreenAsync("Ana Wc-Down");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);

        var first = await _Stubs.ScreenAsync("Ana Wc-Flaky");
        var second = await _Stubs.ScreenAsync("Ana Wc-Flaky");
        var third = await _Stubs.ScreenAsync("Ana Wc-Flaky");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task Each_response_has_its_own_case_id_per_person()
    {
        var one = await StubClient.BodyAsync(await _Stubs.ScreenAsync("Ana Petrova"));
        var again = await StubClient.BodyAsync(await _Stubs.ScreenAsync("Ana Petrova"));
        var other = await StubClient.BodyAsync(await _Stubs.ScreenAsync("Boris Petrov"));

        Assert.Equal((string?)one["caseId"], (string?)again["caseId"]);
        Assert.NotEqual((string?)one["caseId"], (string?)other["caseId"]);
    }

    [Theory]
    [InlineData(null, "1991-03-04", "MB")]
    [InlineData("Ana", null, "MB")]
    [InlineData("Ana", "1991-03-04", null)]
    public async Task Requires_name_date_of_birth_and_nationality(string? name, string? dob,
        string? nationality)
    {
        var response = await _Stubs.Http.PostAsJsonAsync("/worldcheck/v2/screen",
            new { name, dateOfBirth = dob, nationality });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_default_answer_is_not_shared_between_responses()
    {
        // Each response is a copy: changing one must never leak into the next.
        var first = await StubClient.BodyAsync(await _Stubs.ScreenAsync("Ana"));
        first["status"] = "TAMPERED";

        var second = await StubClient.BodyAsync(await _Stubs.ScreenAsync("Ana"));

        Assert.Equal("CLEAR", (string?)second["status"]);
        Assert.IsType<JsonObject>(second);
    }
}
