using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>Builds applications and sends them the way the mobile app does.</summary>
public sealed class Applicant
{
    /// <summary>The market the applicant lives in.</summary>
    public string Market { get; init; } = "MB";

    /// <summary>Given name.</summary>
    public string FirstName { get; init; } = "Ana";

    /// <summary>
    /// Family name. A World-Check marker such as "Possible-Match" here selects that scenario, and
    /// "Card-Down" or "Account-Down" the core banking ones.
    /// </summary>
    public string LastName { get; init; } = "Petrova";

    /// <summary>The national identifier, unique per applicant by default.</summary>
    public string? NationalId { get; init; } = NewNationalId("MB");

    /// <summary>The passport number, for markets that accept it in lieu.</summary>
    public string? PassportNumber { get; init; }

    /// <summary>Email address.</summary>
    public string Email { get; init; } = "ana@example.com";

    /// <summary>Text hidden in the selfie that picks an IDNow scenario, for example
    /// "scenario:inconclusive".</summary>
    public string? IdNowMarker { get; init; }

    /// <summary>Whether the terms were accepted.</summary>
    public bool TermsAccepted { get; init; } = true;

    /// <summary>An identifier in the format of the given market, different every time.</summary>
    public static string NewNationalId(string market)
    {
        var digits = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString();
        return market switch
        {
            "MA" or "MB" => digits + "000"[..3],
            "MC" => "A" + digits[..7] + "Z",
            _ => digits
        };
    }

    /// <summary>Applies the market's own identifier format to a new applicant.</summary>
    public static Applicant For(string market, Action<ApplicantChanges>? change = null)
    {
        var changes = new ApplicantChanges();
        change?.Invoke(changes);
        return new Applicant
        {
            Market = market,
            NationalId = market == "MF" && changes.PassportOnly ? null : NewNationalId(market),
            PassportNumber = changes.PassportOnly ? "P" + Random.Shared.Next(100000, 999999) : null,
            LastName = changes.LastName ?? "Petrova",
            IdNowMarker = changes.IdNowMarker,
            Email = changes.Email ?? "ana@example.com"
        };
    }

    /// <summary>The request body as the app would send it.</summary>
    public string ToJson(Action<JsonObject>? tamper = null)
    {
        var selfie = "selfie-" + Guid.NewGuid() + " " + IdNowMarker;
        var document = "passport-" + Guid.NewGuid();
        var body = new JsonObject
        {
            ["firstName"] = FirstName,
            ["lastName"] = LastName,
            ["dateOfBirth"] = "1991-03-04",
            ["country"] = Market,
            ["nationalId"] = NationalId,
            ["passportNumber"] = PassportNumber,
            ["email"] = Email,
            ["phone"] = "+10000000000",
            ["documents"] = new JsonArray(
                new JsonObject { ["type"] = "PASSPORT", ["image"] = Encode(document) },
                new JsonObject { ["type"] = "SELFIE", ["image"] = Encode(selfie) }),
            ["termsAccepted"] = TermsAccepted
        };
        tamper?.Invoke(body);
        return body.ToJsonString();
    }

    private static string Encode(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}

/// <summary>What a test wants to change about a new applicant.</summary>
public sealed class ApplicantChanges
{
    /// <summary>Gives the applicant a passport number and no national identifier.</summary>
    public bool PassportOnly { get; set; }

    /// <summary>The family name.</summary>
    public string? LastName { get; set; }

    /// <summary>The IDNow scenario marker.</summary>
    public string? IdNowMarker { get; set; }

    /// <summary>The email address.</summary>
    public string? Email { get; set; }
}

/// <summary>What the API answered.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The JSON body.</param>
/// <param name="Headers">The response headers.</param>
/// <param name="Raw">The body as text.</param>
public sealed record ApiAnswer(HttpStatusCode Status, JsonObject Body,
    System.Net.Http.Headers.HttpResponseHeaders Headers, string Raw)
{
    /// <summary>The application's public identifier.</summary>
    public string Id => (string)Body["applicationId"]!;

    /// <summary>The status shown to the applicant.</summary>
    public string State => (string)Body["status"]!;

    /// <summary>The rejection reason, if any.</summary>
    public string? Reason => (string?)Body["reason"];

    /// <summary>The secret for asking about the application later.</summary>
    public string Token => (string)Body["statusToken"]!;
}

/// <summary>Sends requests to the API.</summary>
public static class ApiExtensions
{
    /// <summary>Submits an application.</summary>
    public static Task<ApiAnswer> SubmitAsync(this HttpClient client, string json,
        string? idempotencyKey = null)
    {
        return client.SubmitRawAsync(json, idempotencyKey);
    }

    /// <summary>Submits an application.</summary>
    public static Task<ApiAnswer> SubmitAsync(this HttpClient client, Applicant applicant,
        string? idempotencyKey = null)
    {
        return client.SubmitRawAsync(applicant.ToJson(), idempotencyKey);
    }

    /// <summary>Asks about an application.</summary>
    public static async Task<ApiAnswer> GetStatusAsync(this HttpClient client, string id,
        string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/applications/{id}");
        if (token is not null)
        {
            request.Headers.Add("X-Status-Token", token);
        }

        return await Read(await client.SendAsync(request));
    }

    private static async Task<ApiAnswer> SubmitRawAsync(this HttpClient client, string json,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/applications")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await Read(await client.SendAsync(request));
    }

    private static async Task<ApiAnswer> Read(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw)
            ? new JsonObject()
            : (JsonNode.Parse(raw) as JsonObject ?? new JsonObject());
        return new ApiAnswer(response.StatusCode, body, response.Headers, raw);
    }
}
