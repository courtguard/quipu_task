using System.Net;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>The main flows, end to end: one POST in, one answer out.</summary>
public sealed class SubmissionFlowTests : ApiTestBase
{
    public SubmissionFlowTests(AtlasFixture fixture) : base(fixture)
    {
    }

    [SqlFact]
    public async Task A_clean_applicant_is_approved_with_an_account_and_a_card_in_one_call()
    {
        var before = Fixture.Documents.CountIn("mb-documents");

        var answer = await Client.SubmitAsync(Applicant.For("MB"));

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal("APPROVED", answer.State);
        Assert.StartsWith("MB-", answer.Id);
        Assert.False(string.IsNullOrEmpty(answer.Token));
        Assert.Equal($"/applications/{answer.Id}", answer.Headers.Location?.ToString());

        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.Approved, stored.Status);
        Assert.StartsWith("acc_", stored.AccountId);
        Assert.StartsWith("crd_", stored.CardOrderId);
        Assert.Equal(ScreeningStatus.Clear, stored.ScreeningStatus);
        Assert.Equal(DocumentResult.Valid, stored.DocumentResult);
        Assert.Equal(2, stored.Documents.Count);
        Assert.Equal(before + 2, Fixture.Documents.CountIn("mb-documents"));
        Assert.False(await HasJobAsync(answer.Id), "a finished application needs no more work");
    }

    [SqlFact]
    public async Task The_example_in_the_api_description_is_a_request_that_works()
    {
        // What a reviewer gets from "Try it out" in Swagger, sent unchanged.
        var description = await Client.GetStringAsync("/swagger/v1/swagger.json");
        var example = System.Text.Json.Nodes.JsonNode.Parse(description)!
            ["components"]!["schemas"]!["SubmitApplicationRequest"]!["example"]!;

        var answer = await Client.SubmitAsync(example.ToJsonString());

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal("APPROVED", answer.State);
    }

    [SqlFact]
    public async Task The_status_token_and_idempotency_key_are_read_from_the_declared_headers()
    {
        var applicant = Applicant.For("MB");
        var key = "swagger-" + Guid.NewGuid();

        var first = await Client.SubmitAsync(applicant.ToJson(), key);
        var status = await Client.GetStatusAsync(first.Id, first.Token);

        Assert.Equal(HttpStatusCode.OK, status.Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client.SubmitAsync(Applicant.For("MB"), key)).Status);
    }

    [SqlFact]
    public async Task The_record_is_kept_for_ten_years_from_the_date_of_application()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));

        var stored = await LoadAsync(answer.Id);

        Assert.Equal(stored.CreatedAtUtc.AddYears(10), stored.RetentionUntilUtc);
    }

    [SqlFact]
    public async Task Every_market_keeps_its_data_in_its_own_database_and_container()
    {
        foreach (var code in AtlasFixture.MarketCodes)
        {
            var answer = await Client.SubmitAsync(Applicant.For(code));
            Assert.StartsWith($"{code}-", answer.Id);
        }

        foreach (var code in AtlasFixture.MarketCodes)
        {
            await using var database = Fixture.OpenDatabase(code);
            var markets = await database.Applications.Select(a => a.Market).Distinct()
                .ToListAsync();
            Assert.Equal(new[] { code }, markets);
            Assert.True(Fixture.Documents.CountIn($"{code.ToLowerInvariant()}-documents") >= 2);
        }
    }

    [SqlFact]
    public async Task A_rejected_applicant_is_told_why_and_can_try_again_with_better_photos()
    {
        var first = Applicant.For("MB", c => c.IdNowMarker = "scenario:inconclusive");

        var rejected = await Client.SubmitAsync(first);

        Assert.Equal(HttpStatusCode.Created, rejected.Status);
        Assert.Equal("REJECTED", rejected.State);
        Assert.Equal(RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY, rejected.Reason);

        // Same person, same identifier, clearer photographs.
        var retry = new Applicant { Market = "MB", NationalId = first.NationalId };
        var approved = await Client.SubmitAsync(retry);

        Assert.Equal("APPROVED", approved.State);
        Assert.NotEqual(rejected.Id, approved.Id);
    }

    [SqlFact]
    public async Task A_possible_sanctions_match_is_never_decided_by_the_system()
    {
        var applicant = Applicant.For("MB", c => c.LastName = "Possible-Match");

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Equal("UNDER_REVIEW", answer.State);

        // The applicant is told nothing about why.
        Assert.DoesNotContain("SANCTION", answer.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATCH", answer.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PEP", answer.Raw, StringComparison.OrdinalIgnoreCase);

        await Task.Delay(500);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.PendingComplianceReview, stored.Status);
        Assert.Equal(ScreeningStatus.PossibleMatch, stored.ScreeningStatus);
        Assert.Null(stored.AccountId);
        Assert.Null(stored.CardOrderId);
        Assert.False(await HasJobAsync(answer.Id), "nothing more happens until an officer decides");
    }

    [SqlFact]
    public async Task A_pep_match_is_referred_like_a_sanctions_match()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB", c => c.LastName = "Pep-Match"));

        Assert.Equal("UNDER_REVIEW", answer.State);
    }

    [SqlFact]
    public async Task A_failed_identity_check_outranks_a_possible_match()
    {
        var applicant = Applicant.For("MB", c =>
        {
            c.LastName = "Possible-Match";
            c.IdNowMarker = "scenario:face-mismatch";
        });

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal("REJECTED", answer.State);
        Assert.Equal(RejectionReasons.FACE_MISMATCH, answer.Reason);
    }

    [SqlFact]
    public async Task A_market_that_needs_a_branch_visit_is_not_activated_remotely()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MD"));

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Equal("AWAITING_BRANCH_ACTIVATION", answer.State);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.AwaitingBranchActivation, stored.Status);
        Assert.Null(stored.AccountId);
        Assert.Null(stored.CardOrderId);
    }

    [SqlFact]
    public async Task A_resident_without_a_national_id_can_apply_with_a_passport_number()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MF", c => c.PassportOnly = true));

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal("APPROVED", answer.State);
        var stored = await LoadAsync(answer.Id);
        Assert.Null(stored.NationalId);
        Assert.NotNull(stored.PassportNumber);
    }

    [SqlFact]
    public async Task Nationality_defaults_to_the_country_and_can_be_given()
    {
        var plain = await Client.SubmitAsync(Applicant.For("MB"));
        var given = await Client.SubmitAsync(Applicant.For("MF", c => c.PassportOnly = true)
            .ToJson(body => body["nationality"] = "ua"));

        Assert.Equal("MB", (await LoadAsync(plain.Id)).Nationality);
        Assert.Equal("UA", (await LoadAsync(given.Id)).Nationality);
    }

    [SqlFact]
    public async Task The_status_can_be_read_with_the_token_and_only_with_it()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));
        var other = await Client.SubmitAsync(Applicant.For("MB"));

        var ok = await Client.GetStatusAsync(answer.Id, answer.Token);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal("APPROVED", ok.State);
        Assert.False(ok.Body.ContainsKey("statusToken") && ok.Body["statusToken"] is not null);

        foreach (var attempt in new[]
                 {
                     await Client.GetStatusAsync(answer.Id, null),
                     await Client.GetStatusAsync(answer.Id, "wrong"),
                     await Client.GetStatusAsync(answer.Id, other.Token),
                     await Client.GetStatusAsync("ZZ-" + new string('a', 32), answer.Token),
                     await Client.GetStatusAsync("not-an-id", answer.Token)
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, attempt.Status);
        }
    }

    [SqlFact]
    public async Task A_second_application_from_one_person_is_refused_without_disclosing_the_first()
    {
        var first = Applicant.For("MB", c => c.LastName = "Possible-Match");
        var original = await Client.SubmitAsync(first);

        // Same identifier, different details: not a retry, but another application.
        var second = new Applicant
        {
            Market = "MB", NationalId = first.NationalId, Email = "someone.else@example.com"
        };
        var refused = await Client.SubmitAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("APPLICATION_ALREADY_EXISTS", (string?)refused.Body["error"]);
        Assert.DoesNotContain(original.Id, refused.Raw);
        Assert.DoesNotContain("statusToken", refused.Raw);
    }
}
