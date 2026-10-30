using System.Net;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Onboarding.IntegrationTests.Support;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// What happens when a provider is slow, flaky or down: the applicant is never rejected for it,
/// and the work finishes once the provider recovers.
/// </summary>
public sealed class ResilienceTests : ApiTestBase
{
    // One second to answer, and a single immediate retry. The stand-ins' "flaky" scenarios fail
    // twice, so one immediate retry is not enough and the worker's later retry is what succeeds.
    private static readonly Dictionary<string, string> _ShortWait = new()
    {
        ["Processing:SyncWaitSeconds"] = "1",
        ["Providers:RetryAttempts"] = "1"
    };

    public ResilienceTests(AtlasFixture fixture) : base(fixture, _ShortWait)
    {
    }

    [SqlFact]
    public async Task An_identity_provider_that_is_down_never_rejects_the_applicant()
    {
        var applicant = Applicant.For("MB", c => c.IdNowMarker = "scenario:down");

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Equal("PROCESSING", answer.State);
        Assert.NotNull(answer.Headers.RetryAfter);

        await Task.Delay(800);
        var status = await Client.GetStatusAsync(answer.Id, answer.Token);
        Assert.Equal("PROCESSING", status.State);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.Processing, stored.Status);
        Assert.Null(stored.RejectionReason);
        Assert.True(await HasJobAsync(answer.Id), "the worker keeps trying");
    }

    [SqlFact]
    public async Task A_screening_provider_that_is_down_keeps_what_identity_already_found()
    {
        var applicant = Applicant.For("MB", c => c.LastName = "Wc-Down");

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal("PROCESSING", answer.State);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(DocumentResult.Valid, stored.DocumentResult);
        Assert.Null(stored.ScreeningStatus);
        Assert.Null(stored.AccountId);
    }

    [SqlFact]
    public async Task A_failed_identity_check_rejects_even_when_screening_is_down()
    {
        var applicant = Applicant.For("MB", c =>
        {
            c.LastName = "Wc-Down";
            c.IdNowMarker = "scenario:invalid-document";
        });

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal("REJECTED", answer.State);
        Assert.Equal("DOCUMENT_INVALID", answer.Reason);
    }

    [SqlFact]
    public async Task A_provider_that_fails_twice_is_retried_until_it_answers()
    {
        // The stand-in fails the first two calls for this request; the worker retries.
        var applicant = Applicant.For("MB", c => c.IdNowMarker = "scenario:flaky");
        using var patient = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Processing:SyncWaitSeconds"] = "20",
            ["Providers:RetryAttempts"] = "1"
        });
        using var client = patient.CreateClient();

        var answer = await client.SubmitAsync(applicant);

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal("APPROVED", answer.State);
    }

    [SqlFact]
    public async Task A_brief_failure_is_absorbed_by_the_immediate_retries()
    {
        using var eager = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Processing:SyncWaitSeconds"] = "20",
            ["Providers:RetryAttempts"] = "2"
        });
        using var client = eager.CreateClient();

        var answer = await client.SubmitAsync(
            Applicant.For("MB", c => c.IdNowMarker = "scenario:flaky"));

        Assert.Equal("APPROVED", answer.State);
    }

    [SqlFact]
    public async Task An_application_that_stays_unverified_expires_but_is_not_rejected()
    {
        using var impatient = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Processing:SyncWaitSeconds"] = "1",
            ["Providers:RetryAttempts"] = "1",
            ["Processing:MaxProcessingAgeHours"] = "0.0003"
        });
        using var client = impatient.CreateClient();
        var applicant = Applicant.For("MB", c => c.IdNowMarker = "scenario:down");

        var answer = await client.SubmitAsync(applicant);

        await EventuallyAsync(async () =>
            (await client.GetStatusAsync(answer.Id, answer.Token)).State == "EXPIRED",
            "the application to expire");
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.Expired, stored.Status);
        Assert.Null(stored.RejectionReason);
        Assert.False(await HasJobAsync(answer.Id));

        // Not a judgement on the applicant: they may simply apply again.
        var retry = await client.SubmitAsync(new Applicant
        {
            Market = "MB", NationalId = applicant.NationalId
        });
        Assert.Equal("APPROVED", retry.State);
    }

    [SqlFact]
    public async Task An_application_under_review_does_not_expire()
    {
        using var impatient = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Processing:MaxProcessingAgeHours"] = "0.0003"
        });
        using var client = impatient.CreateClient();
        var answer = await client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));

        await Task.Delay(2000);

        var status = await client.GetStatusAsync(answer.Id, answer.Token);
        Assert.Equal("UNDER_REVIEW", status.State);
    }

    [SqlFact]
    public async Task A_card_order_that_fails_leaves_the_account_open_and_the_application_pending()
    {
        var applicant = Applicant.For("MB", c => c.LastName = "Card-Down");

        var answer = await Client.SubmitAsync(applicant);

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Equal("PROCESSING", answer.State);
        var first = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.Fulfilling, first.Status);
        Assert.StartsWith("acc_", first.AccountId);
        Assert.Null(first.CardOrderId);

        // Still retrying, still the same single account, and never approved without a card.
        await Task.Delay(800);
        var later = await LoadAsync(answer.Id);
        Assert.Equal(first.AccountId, later.AccountId);
        Assert.Equal(ApplicationStatus.Fulfilling, later.Status);
        Assert.True(await HasJobAsync(answer.Id));
    }

    [SqlFact]
    public async Task A_card_order_that_recovers_completes_the_application_with_the_same_account()
    {
        using var patient = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Processing:SyncWaitSeconds"] = "20",
            ["Providers:RetryAttempts"] = "1"
        });
        using var client = patient.CreateClient();

        var answer = await client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Card-Flaky"));

        Assert.Equal("APPROVED", answer.State);
        var stored = await LoadAsync(answer.Id);
        Assert.StartsWith("acc_", stored.AccountId);
        Assert.StartsWith("crd_", stored.CardOrderId);
    }

    [SqlFact]
    public async Task An_account_that_cannot_be_opened_leaves_the_application_pending()
    {
        var answer = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Account-Down"));

        Assert.Equal("PROCESSING", answer.State);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(ApplicationStatus.Fulfilling, stored.Status);
        Assert.Null(stored.AccountId);
    }

}
