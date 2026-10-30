using System.Net;
using Atlas.Onboarding.Infrastructure.Retention;
using Atlas.Onboarding.IntegrationTests.Support;
using Atlas.Review.Api;
using Atlas.Shared.Applications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// Erasure against retention: a request restricts the record at once, and every record is
/// deleted, with everything about it, when its ten-year retention period ends.
/// </summary>
public sealed class ErasureAndRetentionTests : ApiTestBase
{
    private readonly WebApplicationFactory<ReviewApiMarker>? _Review;

    public ErasureAndRetentionTests(AtlasFixture fixture)
        : base(fixture, new Dictionary<string, string>
        {
            ["Processing:SyncWaitSeconds"] = "1",
            ["Providers:RetryAttempts"] = "1"
        })
    {
        if (TestSql.IsAvailable)
        {
            _Review = fixture.CreateReviewApi();
        }
    }

    private Task<StaffClient> DpoAsync(params string[] markets)
        => StaffClient.SignInAsync(Fixture, _Review!, "dpo-1", "data_protection_officer",
            markets);

    private static Task<(HttpStatusCode Status, System.Text.Json.Nodes.JsonNode? Body,
        string Raw)> RequestErasureAsync(StaffClient dpo, string id, string? reference = "DSR-1")
        => dpo.PostAsync($"/erasure-requests/{id}", new { reference });

    private Task WaitUntilRestrictedAsync(string id)
        => EventuallyAsync(async () => (await LoadAsync(id)).IsRestricted,
            "the record to be restricted");

    [SqlFact]
    public async Task An_erasure_request_restricts_the_record_and_locks_the_status_token()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));
        await EventuallyAsync(async () =>
            (await Client.GetStatusAsync(answer.Id, answer.Token)).State == "APPROVED",
            "the application to be approved");
        using var dpo = await DpoAsync("MB");

        var recorded = await RequestErasureAsync(dpo, answer.Id);

        Assert.Equal(HttpStatusCode.Accepted, recorded.Status);
        await WaitUntilRestrictedAsync(answer.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.GetStatusAsync(answer.Id, answer.Token)).Status);
        var stored = await LoadAsync(answer.Id);
        Assert.Equal(stored.CreatedAtUtc.AddYears(10), stored.RetentionUntilUtc);
        Assert.Contains(("staff:dpo-1", "DECISION_RECORDED"),
            await Fixture.AccessLogAsync(answer.Id));
    }

    [SqlFact]
    public async Task An_erasure_request_stops_an_application_that_is_still_being_processed()
    {
        var answer = await Client.SubmitAsync(
            Applicant.For("MB", c => c.IdNowMarker = "scenario:down"));
        Assert.Equal("PROCESSING", answer.State);
        using var dpo = await DpoAsync("MB");

        await RequestErasureAsync(dpo, answer.Id);
        await WaitUntilRestrictedAsync(answer.Id);
        await EventuallyAsync(async () => !await HasJobAsync(answer.Id),
            "the work on the application to stop");

        // Nothing more is sent anywhere once the record is restricted.
        var sentBefore = (await Fixture.AccessLogAsync(answer.Id, "service:atlas-onboarding"))
            .Count(e => e.Action == "SENT_TO_IDENTITY_PROVIDER");
        await Task.Delay(800);
        var sentAfter = (await Fixture.AccessLogAsync(answer.Id, "service:atlas-onboarding"))
            .Count(e => e.Action == "SENT_TO_IDENTITY_PROVIDER");
        Assert.Equal(sentBefore, sentAfter);
    }

    [SqlFact]
    public async Task A_restricted_case_disappears_from_the_compliance_queue()
    {
        var referred = await Client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        using var dpo = await DpoAsync("MB");
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");
        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync($"/reviews/{referred.Id}")).Status);

        await RequestErasureAsync(dpo, referred.Id);
        await WaitUntilRestrictedAsync(referred.Id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await officer.GetAsync($"/reviews/{referred.Id}")).Status);
        Assert.DoesNotContain(referred.Id, (await officer.GetAsync("/reviews")).Raw);
    }

    [SqlFact]
    public async Task Only_a_data_protection_officer_of_the_market_can_request_erasure()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));
        using var officer = await StaffClient.SignInAsync(Fixture, _Review!, "officer-1",
            "compliance_officer", "MB");
        using var elsewhere = await DpoAsync("MA");
        using var dpo = await DpoAsync("MB");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await RequestErasureAsync(officer, answer.Id)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await RequestErasureAsync(elsewhere, answer.Id)).Status);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RequestErasureAsync(dpo, answer.Id, reference: null)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestErasureAsync(dpo, answer.Id)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await RequestErasureAsync(dpo, answer.Id)).Status);
    }

    [SqlFact]
    public async Task Records_are_deleted_with_everything_about_them_when_retention_ends()
    {
        var plain = await Client.SubmitAsync(Applicant.For("ME"));
        var restricted = await Client.SubmitAsync(
            Applicant.For("ME", c => c.LastName = "Possible-Match"));
        using var dpo = await DpoAsync("ME");
        await RequestErasureAsync(dpo, restricted.Id);
        await WaitUntilRestrictedAsync(restricted.Id);
        await EventuallyAsync(async () =>
            (await Fixture.AccessLogAsync(plain.Id)).Count > 0, "work on the first application");
        var purger = Services.GetRequiredService<RetentionPurger>();

        var deleted = await purger.PurgeAsync(Fixture.Market("ME"),
            DateTime.UtcNow.AddYears(10).AddDays(1), CancellationToken.None);

        Assert.True(deleted >= 2);
        Assert.Equal(0, Fixture.Documents.CountIn("me-documents"));
        await using var database = Fixture.OpenDatabase("ME");
        var ids = new[] { plain.Id, restricted.Id }
            .Select(id => ApplicationIds.Parse(id)!.Value.Id).ToList();
        Assert.False(await database.Applications.AnyAsync(a => ids.Contains(a.Id)));
        Assert.False(await database.Documents.AnyAsync(d => ids.Contains(d.ApplicationId)));
        Assert.False(await database.StaffDecisions.AnyAsync(d => ids.Contains(d.ApplicationId)));
        Assert.False(await database.AccessLog.AnyAsync(e => ids.Contains(e.ApplicationId)));
        Assert.False(await database.Jobs.AnyAsync(j => ids.Contains(j.ApplicationId)));
    }

    [SqlFact]
    public async Task Nothing_is_deleted_before_its_retention_period_ends()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));
        var purger = Services.GetRequiredService<RetentionPurger>();

        var deleted = await purger.PurgeAsync(Fixture.Market("MB"),
            DateTime.UtcNow.AddYears(10).AddDays(-1), CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.NotNull(await LoadAsync(answer.Id));
        Assert.NotEmpty(await Fixture.AccessLogAsync(answer.Id));
    }
}
