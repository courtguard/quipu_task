using System.Net;
using System.Text.Json.Nodes;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>Bad submissions are refused with codes, and nothing is stored.</summary>
public sealed class ValidationTests : ApiTestBase
{
    public ValidationTests(AtlasFixture fixture) : base(fixture)
    {
    }

    private async Task<ApiAnswer> SubmitInvalidAsync(string market, Action<JsonObject> tamper)
    {
        return await Client.SubmitAsync(Applicant.For(market).ToJson(tamper));
    }

    private static IEnumerable<string> Codes(ApiAnswer answer)
    {
        return ((JsonArray)answer.Body["errors"]!)
            .Select(e => $"{(string?)e!["field"]}:{(string?)e["code"]}");
    }

    [SqlFact]
    public async Task A_malformed_national_id_is_refused_without_repeating_it()
    {
        var answer = await SubmitInvalidAsync("MB", b => b["nationalId"] = "SECRETVALUE");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("VALIDATION_FAILED", (string?)answer.Body["error"]);
        Assert.Contains("nationalId:NATIONAL_ID_INVALID_FORMAT", Codes(answer));
        Assert.DoesNotContain("SECRETVALUE", answer.Raw);
    }

    [SqlFact]
    public async Task Every_market_needs_some_identifier()
    {
        var answer = await SubmitInvalidAsync("MF", b => b["nationalId"] = null);

        Assert.Contains("nationalId:IDENTIFIER_REQUIRED", Codes(answer));
    }

    [SqlFact]
    public async Task A_malformed_passport_number_is_refused()
    {
        var answer = await SubmitInvalidAsync("MF", b =>
        {
            b["nationalId"] = null;
            b["passportNumber"] = "!!";
        });

        Assert.Contains("passportNumber:PASSPORT_NUMBER_INVALID_FORMAT", Codes(answer));
    }

    [SqlFact]
    public async Task An_unknown_country_is_refused()
    {
        var answer = await SubmitInvalidAsync("MB", b => b["country"] = "ZZ");

        Assert.Contains("country:NOT_SUPPORTED", Codes(answer));
    }

    [SqlFact]
    public async Task Exactly_one_document_and_one_selfie_are_required()
    {
        var withoutSelfie = await SubmitInvalidAsync("MB",
            b => ((JsonArray)b["documents"]!).RemoveAt(1));
        var twoSelfies = await SubmitInvalidAsync("MB",
            b => b["documents"]![0]!["type"] = "SELFIE");
        var unknownType = await SubmitInvalidAsync("MB",
            b => b["documents"]![0]!["type"] = "DRIVING_LICENCE");

        foreach (var answer in new[] { withoutSelfie, twoSelfies, unknownType })
        {
            Assert.Contains("documents:EXPECTED_ONE_DOCUMENT_AND_ONE_SELFIE", Codes(answer));
        }
    }

    [SqlFact]
    public async Task An_image_that_is_not_base64_is_refused()
    {
        var answer = await SubmitInvalidAsync("MB",
            b => b["documents"]![0]!["image"] = "not base64 !!");

        Assert.Contains("documents[0].image:INVALID_IMAGE", Codes(answer));
    }

    [SqlFact]
    public async Task The_terms_must_have_been_accepted()
    {
        var answer = await Client.SubmitAsync(new Applicant { TermsAccepted = false }
            .ToJson());

        Assert.Contains("termsAccepted:MUST_BE_TRUE", Codes(answer));
    }

    [SqlFact]
    public async Task A_date_of_birth_in_the_future_is_refused()
    {
        var answer = await SubmitInvalidAsync("MB", b => b["dateOfBirth"] = "2999-01-01");

        Assert.Contains("dateOfBirth:INVALID", Codes(answer));
    }

    [SqlFact]
    public async Task Every_problem_is_reported_at_once()
    {
        var answer = await SubmitInvalidAsync("MB", b =>
        {
            b["firstName"] = "";
            b["email"] = "nope";
            b["phone"] = "x";
            b["termsAccepted"] = false;
        });

        var codes = Codes(answer).ToList();
        Assert.Contains("firstName:REQUIRED", codes);
        Assert.Contains("email:INVALID", codes);
        Assert.Contains("phone:INVALID", codes);
        Assert.Contains("termsAccepted:MUST_BE_TRUE", codes);
    }

    [SqlFact]
    public async Task A_body_that_is_not_json_is_refused()
    {
        var answer = await Client.SubmitAsync("{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("INVALID_JSON", (string?)answer.Body["error"]);
    }

    [SqlFact]
    public async Task A_refused_submission_stores_nothing()
    {
        await using var database = Fixture.OpenDatabase("MB");
        var applications = await database.Applications.CountAsync();
        var documents = Fixture.Documents.CountIn("mb-documents");

        await SubmitInvalidAsync("MB", b => b["nationalId"] = "bad");

        Assert.Equal(applications, await database.Applications.CountAsync());
        Assert.Equal(documents, Fixture.Documents.CountIn("mb-documents"));
    }
}
