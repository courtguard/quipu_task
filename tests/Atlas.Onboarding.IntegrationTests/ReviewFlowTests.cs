using System.Net;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Onboarding.IntegrationTests.Support;
using Atlas.Review.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// The paths that need a person, end to end: a compliance officer decides a referred
/// application, and branch staff confirm an in-person visit. The onboarding worker then
/// carries the application on.
/// </summary>
public sealed class ReviewFlowTests : ApiTestBase
{
    private readonly WebApplicationFactory<ReviewApiMarker>? _Review;

    public ReviewFlowTests(AtlasFixture fixture) : base(fixture)
    {
        if (TestSql.IsAvailable)
        {
            _Review = fixture.CreateReviewApi();
        }
    }

    private Task<StaffClient> OfficerAsync(params string[] markets)
        => StaffClient.SignInAsync(Fixture, _Review!, "officer-1", "compliance_officer", markets);

    private Task<StaffClient> BranchStaffAsync(params string[] markets)
        => StaffClient.SignInAsync(Fixture, _Review!, "branch-1", "branch_staff", markets);

    private async Task<string> EventualStatusAsync(ApiAnswer answer, string expected)
    {
        var last = "";
        await EventuallyAsync(async () =>
        {
            last = (await Client.GetStatusAsync(answer.Id, answer.Token)).State;
            return last == expected;
        }, $"the application to become {expected}");
        return last;
    }

    [SqlFact]
    public async Task An_officer_clears_a_possible_match_and_the_account_is_then_opened()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        Assert.Equal("UNDER_REVIEW", referred.State);
        using var officer = await OfficerAsync("MB");

        var queue = await officer.GetAsync("/reviews?market=MB");
        Assert.Equal(HttpStatusCode.OK, queue.Status);
        Assert.Contains(referred.Id, queue.Raw);
        Assert.DoesNotContain("Possible-Match", queue.Raw);

        var details = await officer.GetAsync($"/reviews/{referred.Id}");
        Assert.Equal(HttpStatusCode.OK, details.Status);
        Assert.Equal("Possible-Match", (string?)details.Body!["lastName"]);
        Assert.StartsWith("wc_", (string?)details.Body["screeningCaseId"]);

        var decided = await officer.DecideAsync(referred.Id, "APPROVE");
        Assert.Equal(HttpStatusCode.Accepted, decided.Status);

        await EventualStatusAsync(referred, "APPROVED");
        var stored = await LoadAsync(referred.Id);
        Assert.StartsWith("acc_", stored.AccountId);
        Assert.StartsWith("crd_", stored.CardOrderId);
    }

    [SqlFact]
    public async Task An_officer_declines_and_the_applicant_learns_nothing_about_why()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        using var officer = await OfficerAsync("MB");

        await officer.DecideAsync(referred.Id, "REJECT");

        await EventualStatusAsync(referred, "REJECTED");
        var status = await Client.GetStatusAsync(referred.Id, referred.Token);
        Assert.Equal(RejectionReasons.DECLINED, status.Reason);
        Assert.DoesNotContain("MATCH", status.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await LoadAsync(referred.Id)).AccountId);
    }

    [SqlFact]
    public async Task A_cleared_in_person_applicant_is_activated_after_the_branch_visit()
    {
        var applicant = await Client.SubmitAsync(Applicant.For("MD"));
        Assert.Equal("AWAITING_BRANCH_ACTIVATION", applicant.State);
        using var branch = await BranchStaffAsync("MD");

        var waiting = await branch.GetAsync("/branch-activations?market=MD");
        Assert.Contains(applicant.Id, waiting.Raw);
        var details = await branch.GetAsync($"/branch-activations/{applicant.Id}");
        Assert.Equal(HttpStatusCode.OK, details.Status);

        var activated = await branch.PostAsync($"/branch-activations/{applicant.Id}",
            new { note = "Passport seen, contract signed" });
        Assert.Equal(HttpStatusCode.Accepted, activated.Status);

        await EventualStatusAsync(applicant, "APPROVED");
        Assert.StartsWith("acc_", (await LoadAsync(applicant.Id)).AccountId);
    }

    [SqlFact]
    public async Task A_referred_in_person_applicant_needs_the_officer_and_then_the_branch()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MD", c => c.LastName = "Pep-Match"));
        using var officer = await OfficerAsync("MD");
        using var branch = await BranchStaffAsync("MD");

        // Branch staff cannot activate while the review is open.
        var tooEarly = await branch.PostAsync($"/branch-activations/{referred.Id}", new { });
        Assert.Equal(HttpStatusCode.NotFound, tooEarly.Status);

        await officer.DecideAsync(referred.Id, "APPROVE");
        await EventualStatusAsync(referred, "AWAITING_BRANCH_ACTIVATION");

        await branch.PostAsync($"/branch-activations/{referred.Id}", new { });
        await EventualStatusAsync(referred, "APPROVED");
    }

    [SqlFact]
    public async Task Every_step_is_in_the_access_log_with_who_did_it()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        using var officer = await OfficerAsync("MB");
        await officer.GetAsync($"/reviews/{referred.Id}");
        await officer.DecideAsync(referred.Id, "APPROVE");
        await EventualStatusAsync(referred, "APPROVED");

        var log = await Fixture.AccessLogAsync(referred.Id);

        Assert.Contains(("applicant", "SUBMITTED"), log);
        Assert.Contains(("service:atlas-onboarding", "SENT_TO_IDENTITY_PROVIDER"), log);
        Assert.Contains(("service:atlas-onboarding", "SENT_TO_SCREENING_PROVIDER"), log);
        Assert.Contains(("staff:officer-1", "CASE_VIEWED"), log);
        Assert.Contains(("staff:officer-1", "DECISION_RECORDED"), log);
        Assert.Contains(("service:atlas-onboarding", "DECISION_APPLIED"), log);
        Assert.Contains(("service:atlas-onboarding", "SENT_TO_CORE_BANKING"), log);
        Assert.Contains(("applicant", "STATUS_READ"), log);
    }

    [SqlFact]
    public async Task The_applied_decision_names_the_officer()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        using var officer = await OfficerAsync("MB");

        await officer.DecideAsync(referred.Id, "APPROVE");
        await EventualStatusAsync(referred, "APPROVED");

        var decision = await Fixture.DecisionAsync(referred.Id);
        Assert.Equal("staff:officer-1", decision.DecidedBy);
        Assert.Equal("APPLIED", decision.Outcome);
        Assert.Equal(ApplicationStatus.Approved, (await LoadAsync(referred.Id)).Status);
    }
}
