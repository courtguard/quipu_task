using System.Net;
using Atlas.Onboarding.IntegrationTests.Support;
using Atlas.Review.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// Who may see and decide what. The onboarding worker is off here, so a recorded decision stays
/// unapplied and every check is deterministic.
/// </summary>
public sealed class ReviewAccessTests : ApiTestBase
{
    private readonly WebApplicationFactory<ReviewApiMarker>? _Review;

    public ReviewAccessTests(AtlasFixture fixture)
        : base(fixture, new Dictionary<string, string>
        {
            ["Processing:WorkerEnabled"] = "false",
            ["Processing:SyncWaitSeconds"] = "1"
        })
    {
        if (TestSql.IsAvailable)
        {
            _Review = fixture.CreateReviewApi();
        }
    }

    // Without the worker, a submission stays in processing; this puts one straight under review
    // by running the worker once by hand.
    private async Task<string> ReferredAsync(string market)
    {
        using var withWorker = Fixture.CreateApi();
        using var client = withWorker.CreateClient();
        var answer = await client.SubmitAsync(
            Applicant.For(market, c => c.LastName = "Possible-Match"));
        Assert.Equal("UNDER_REVIEW", answer.State);
        return answer.Id;
    }

    [SqlFact]
    public async Task Without_a_valid_token_nothing_is_accessible()
    {
        var id = await ReferredAsync("MB");
        var valid = await Fixture.StaffTokenAsync("officer-1", "compliance_officer",
            new[] { "MB" });
        var expired = await Fixture.StaffTokenAsync("officer-1", "compliance_officer",
            new[] { "MB" }, lifetimeMinutes: -5);
        var tampered = valid[..^4] + (valid.EndsWith("AAAA") ? "BBBB" : "AAAA");

        foreach (var token in new[] { null, "not-a-token", expired, tampered })
        {
            using var client = new StaffClient(_Review!.CreateClient(), token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/reviews")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.GetAsync($"/reviews/{id}")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.DecideAsync(id, "APPROVE")).Status);
        }
    }

    [SqlFact]
    public async Task An_officer_from_another_market_cannot_see_or_decide_the_case()
    {
        var id = await ReferredAsync("MB");
        using var elsewhere = await StaffClient.SignInAsync(Fixture, _Review!, "officer-ma",
            "compliance_officer", "MA");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await elsewhere.GetAsync("/reviews?market=MB")).Status);
        Assert.DoesNotContain(id, (await elsewhere.GetAsync("/reviews")).Raw);
        Assert.Equal(HttpStatusCode.NotFound, (await elsewhere.GetAsync($"/reviews/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await elsewhere.DecideAsync(id, "APPROVE")).Status);
        Assert.Empty(await Fixture.AccessLogAsync(id, "staff:officer-ma"));
    }

    [SqlFact]
    public async Task Each_role_can_only_do_its_own_job()
    {
        var id = await ReferredAsync("MB");
        using var branch = await StaffClient.SignInAsync(Fixture, _Review!, "branch-1",
            "branch_staff", "MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");

        Assert.Equal(HttpStatusCode.Forbidden, (await branch.GetAsync("/reviews")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await branch.DecideAsync(id, "APPROVE")).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await officer.PostAsync($"/branch-activations/{id}", new { })).Status);
    }

    [SqlFact]
    public async Task A_case_can_only_be_decided_once()
    {
        var id = await ReferredAsync("MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");
        using var colleague = await StaffClient.SignInAsync(Fixture, _Review!, "officer-2",
            "compliance_officer", "MB");

        var first = await officer.DecideAsync(id, "APPROVE");
        var second = await colleague.DecideAsync(id, "REJECT");

        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        Assert.Equal("ALREADY_DECIDED", (string?)second.Body!["error"]);
    }

    [SqlFact]
    public async Task A_decision_must_be_approve_or_reject()
    {
        var id = await ReferredAsync("MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");

        var answer = await officer.DecideAsync(id, "MAYBE");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
    }

    [SqlFact]
    public async Task A_decision_waits_for_the_worker_and_is_not_applied_by_the_review_api()
    {
        var id = await ReferredAsync("MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");

        await officer.DecideAsync(id, "APPROVE");

        // Only the onboarding service changes applications; with its worker off, nothing moves.
        await Task.Delay(500);
        Assert.Equal("PendingComplianceReview", (await LoadAsync(id)).Status.ToString());
        Assert.Null((await Fixture.DecisionAsync(id)).AppliedAtUtc);
    }

    [SqlFact]
    public async Task Listing_cases_shows_no_personal_data_and_is_not_logged_as_a_view()
    {
        var id = await ReferredAsync("MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");

        var list = await officer.GetAsync("/reviews");

        Assert.Contains(id, list.Raw);
        Assert.DoesNotContain("firstName", list.Raw);
        Assert.DoesNotContain("nationalId", list.Raw);
        Assert.Empty(await Fixture.AccessLogAsync(id, "staff:officer-1"));
    }
}
