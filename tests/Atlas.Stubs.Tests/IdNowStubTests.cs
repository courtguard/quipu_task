using System.Net;
using System.Net.Http.Json;
using Atlas.Stubs.Tests.Support;

namespace Atlas.Stubs.Tests;

public sealed class IdNowStubTests : IDisposable
{
    private readonly StubClient _Stubs = new();

    public void Dispose() => _Stubs.Dispose();

    [Fact]
    public async Task An_ordinary_image_gets_the_happy_path()
    {
        var response = await _Stubs.IdentifyAsync("just some photo bytes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await StubClient.BodyAsync(response);
        Assert.Equal("VALID", (string?)body["documentResult"]);
        Assert.True((bool?)body["faceMatch"]);
        Assert.Equal(0.97, (double?)body["confidence"]);
        Assert.StartsWith("idn_", (string?)body["identificationId"]);
    }

    [Theory]
    [InlineData("scenario:invalid-document", "INVALID", true, 0.30)]
    [InlineData("scenario:face-mismatch", "VALID", false, 0.40)]
    [InlineData("scenario:inconclusive", "INCONCLUSIVE", true, 0.50)]
    [InlineData("scenario:low-confidence", "VALID", true, 0.60)]
    [InlineData("SCENARIO:INCONCLUSIVE", "INCONCLUSIVE", true, 0.50)]
    public async Task A_marker_in_the_document_selects_the_scenario(string marker, string result,
        bool face, double confidence)
    {
        var response = await _Stubs.IdentifyAsync($"binary-ish {marker} more bytes");

        var body = await StubClient.BodyAsync(response);
        Assert.Equal(result, (string?)body["documentResult"]);
        Assert.Equal(face, (bool?)body["faceMatch"]);
        Assert.Equal(confidence, (double?)body["confidence"]);
    }

    [Fact]
    public async Task A_marker_in_the_selfie_works_too()
    {
        var response = await _Stubs.IdentifyAsync("passport photo", "scenario:face-mismatch");

        var body = await StubClient.BodyAsync(response);
        Assert.False((bool?)body["faceMatch"]);
    }

    [Fact]
    public async Task Real_binary_images_are_searched_without_error()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var response = await _Stubs.Http.PostAsJsonAsync("/idnow/v1/identifications", new
        {
            documentType = "ID_CARD",
            documentImage = Convert.ToBase64String(bytes),
            selfieImage = Convert.ToBase64String(bytes)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_down_provider_always_answers_503()
    {
        for (var i = 0; i < 4; i++)
        {
            var response = await _Stubs.IdentifyAsync("scenario:down");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_flaky_provider_fails_twice_then_recovers_for_the_same_request()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await _Stubs.IdentifyAsync("scenario:flaky one")).StatusCode);
        }

        Assert.Equal(
            new[]
            {
                HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.OK, HttpStatusCode.OK
            },
            statuses);
    }

    [Fact]
    public async Task Flakiness_is_counted_per_request_not_across_applicants()
    {
        await _Stubs.IdentifyAsync("scenario:flaky one");
        await _Stubs.IdentifyAsync("scenario:flaky one");

        // A different applicant's first call still fails: counts are not shared.
        var other = await _Stubs.IdentifyAsync("scenario:flaky two");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
    }

    [Fact]
    public async Task Reset_makes_a_flaky_scenario_start_over()
    {
        await _Stubs.IdentifyAsync("scenario:flaky one");
        await _Stubs.IdentifyAsync("scenario:flaky one");
        await _Stubs.ResetAsync();

        var response = await _Stubs.IdentifyAsync("scenario:flaky one");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("DRIVING_LICENCE")]
    [InlineData("")]
    public async Task Rejects_an_unsupported_document_type(string documentType)
    {
        var response = await _Stubs.IdentifyAsync("photo", documentType: documentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_images_that_are_not_base64()
    {
        var response = await _Stubs.Http.PostAsJsonAsync("/idnow/v1/identifications", new
        {
            documentType = "PASSPORT",
            documentImage = "not base64 !!",
            selfieImage = StubClient.Image("selfie")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
