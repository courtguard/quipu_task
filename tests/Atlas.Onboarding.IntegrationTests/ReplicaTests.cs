using System.Net;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.IntegrationTests.Support;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// Work is never tied to the replica that received the request: any replica with a worker can
/// finish it.
/// </summary>
public sealed class ReplicaTests : ApiTestBase
{
    // This replica accepts requests but runs no worker, and gives up waiting after a second.
    private static readonly Dictionary<string, string> _NoWorker = new()
    {
        ["Processing:WorkerEnabled"] = "false",
        ["Processing:SyncWaitSeconds"] = "1"
    };

    public ReplicaTests(AtlasFixture fixture) : base(fixture, _NoWorker)
    {
    }

    [SqlFact]
    public async Task Another_replicas_worker_completes_an_application_this_one_could_not()
    {
        var answer = await Client.SubmitAsync(Applicant.For("MB"));

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Equal("PROCESSING", answer.State);
        Assert.Equal(ApplicationStatus.Processing, (await LoadAsync(answer.Id)).Status);

        // A second replica, with a worker, picks the job up from the shared queue.
        using var withWorker = Fixture.CreateApi();
        using var _ = withWorker.CreateClient();

        await EventuallyAsync(async () =>
            (await LoadAsync(answer.Id)).Status == ApplicationStatus.Approved,
            "the other replica to complete the application");
        var status = await Client.GetStatusAsync(answer.Id, answer.Token);
        Assert.Equal("APPROVED", status.State);
    }
}
