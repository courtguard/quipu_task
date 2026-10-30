using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Atlas.Review.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>A signed-in member of staff calling the Review API.</summary>
public sealed class StaffClient : IDisposable
{
    private readonly HttpClient _Http;

    /// <summary>Signs the staff member in and connects to the API.</summary>
    public static async Task<StaffClient> SignInAsync(AtlasFixture fixture,
        WebApplicationFactory<ReviewApiMarker> api, string subject, string role,
        params string[] markets)
    {
        var token = await fixture.StaffTokenAsync(subject, role, markets);
        return new StaffClient(api.CreateClient(), token);
    }

    /// <summary>Connects with a given token, or none.</summary>
    public StaffClient(HttpClient http, string? token)
    {
        _Http = http;
        if (token is not null)
        {
            _Http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>GETs a path and reads the answer.</summary>
    public async Task<(HttpStatusCode Status, JsonNode? Body, string Raw)> GetAsync(string path)
    {
        return await ReadAsync(await _Http.GetAsync(path));
    }

    /// <summary>POSTs a JSON body to a path and reads the answer.</summary>
    public async Task<(HttpStatusCode Status, JsonNode? Body, string Raw)> PostAsync(string path,
        object body)
    {
        return await ReadAsync(await _Http.PostAsJsonAsync(path, body));
    }

    /// <summary>Records a compliance decision.</summary>
    public Task<(HttpStatusCode Status, JsonNode? Body, string Raw)> DecideAsync(string id,
        string decision)
    {
        return PostAsync($"/reviews/{id}/decision", new { decision, note = "checked" });
    }

    public void Dispose() => _Http.Dispose();

    private static async Task<(HttpStatusCode, JsonNode?, string)> ReadAsync(
        HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
        return (response.StatusCode, body, raw);
    }
}
