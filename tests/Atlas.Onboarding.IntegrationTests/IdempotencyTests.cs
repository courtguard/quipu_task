using System.Net;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// A request that arrives twice (a retry after a tunnel, a double tap, two replicas at once)
/// creates one application, one account and one card.
/// </summary>
public sealed class IdempotencyTests : ApiTestBase
{
    public IdempotencyTests(AtlasFixture fixture) : base(fixture)
    {
    }

    private async Task<int> CountAsync(string nationalId)
    {
        await using var database = Fixture.OpenDatabase("MB");
        return await database.Applications.CountAsync(a => a.NationalId == nationalId);
    }

    [SqlFact]
    public async Task Sending_the_same_request_twice_returns_the_same_application()
    {
        var applicant = Applicant.For("MB");
        var json = applicant.ToJson();

        var first = await Client.SubmitAsync(json);
        var again = await Client.SubmitAsync(json);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Token, again.Token);
        Assert.Equal(first.Status, again.Status);
        Assert.Equal(1, await CountAsync(applicant.NationalId!));
    }

    [SqlFact]
    public async Task A_client_supplied_key_makes_a_retry_recognisable()
    {
        var applicant = Applicant.For("MB");
        var json = applicant.ToJson();
        var key = "mobile-" + Guid.NewGuid();

        var first = await Client.SubmitAsync(json, key);
        var again = await Client.SubmitAsync(json, key);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(1, await CountAsync(applicant.NationalId!));
    }

    [SqlFact]
    public async Task Reusing_a_key_for_a_different_request_is_refused()
    {
        var key = "mobile-" + Guid.NewGuid();
        await Client.SubmitAsync(Applicant.For("MB"), key);

        var refused = await Client.SubmitAsync(Applicant.For("MB"), key);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (string?)refused.Body["error"]);
    }

    [SqlFact]
    public async Task A_key_with_odd_characters_is_refused()
    {
        var refused = await Client.SubmitAsync(Applicant.For("MB"), "bad key!");

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", (string?)refused.Body["error"]);
    }

    [SqlFact]
    public async Task Eight_identical_requests_at_once_create_one_application_and_one_account()
    {
        var applicant = Applicant.For("MB");
        var json = applicant.ToJson();

        var answers = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Client.SubmitAsync(json)));

        Assert.All(answers, a => Assert.Equal(answers[0].Id, a.Id));
        Assert.All(answers, a => Assert.Equal("APPROVED", a.State));
        Assert.Equal(1, await CountAsync(applicant.NationalId!));
    }

    [SqlFact]
    public async Task Six_different_requests_from_one_person_at_once_create_only_one_application()
    {
        var nationalId = Applicant.NewNationalId("MB");
        var requests = Enumerable.Range(0, 6).Select(i => new Applicant
        {
            Market = "MB", NationalId = nationalId, Email = $"person{i}@example.com"
        });

        var answers = await Task.WhenAll(requests.Select(a => Client.SubmitAsync(a)));

        Assert.Equal(1, answers.Count(a => a.Status is HttpStatusCode.Created
            or HttpStatusCode.Accepted));
        Assert.Equal(5, answers.Count(a => a.Status == HttpStatusCode.Conflict));
        Assert.Equal(1, await CountAsync(nationalId));
    }

    [SqlFact]
    public async Task Two_replicas_racing_on_the_same_request_still_create_one_application()
    {
        using var otherReplica = Fixture.CreateApi();
        using var otherClient = otherReplica.CreateClient();
        var applicant = Applicant.For("MB");
        var json = applicant.ToJson();

        var answers = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? Client : otherClient)
                .SubmitAsync(json)));

        Assert.All(answers, a => Assert.Equal(answers[0].Id, a.Id));
        Assert.Equal(1, await CountAsync(applicant.NationalId!));
    }
}
