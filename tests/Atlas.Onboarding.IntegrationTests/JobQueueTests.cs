using System.Collections.Concurrent;
using Atlas.Onboarding.Infrastructure.Jobs;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// The queue that lets any number of replicas share the work: no job is taken twice, and a job
/// whose worker crashed is taken by another.
/// </summary>
[Collection("atlas")]
public sealed class JobQueueTests : IDisposable
{
    private static readonly TimeSpan _Lease = TimeSpan.FromHours(1);
    private readonly AtlasFixture _Fixture;

    public JobQueueTests(AtlasFixture fixture)
    {
        _Fixture = fixture;
    }

    public void Dispose()
    {
        if (TestSql.IsAvailable)
        {
            _Fixture.PurgeJobs();
        }
    }

    private async Task<List<Guid>> SeedAsync(int count, DateTime due)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await using var database = _Fixture.OpenDatabase("MB");
        database.Jobs.AddRange(ids.Select(id => new ProcessingJob(id, due)));
        await database.SaveChangesAsync();
        return ids;
    }

    private async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(string worker, DateTime now,
        int max = 100)
    {
        await using var database = _Fixture.OpenDatabase("MB");
        return await JobQueue.ClaimDueAsync(database, worker, now, _Lease, max,
            CancellationToken.None);
    }

    [SqlFact]
    public async Task Workers_competing_for_jobs_never_take_the_same_one()
    {
        var now = DateTime.UtcNow;
        var seeded = await SeedAsync(80, now.AddMinutes(-1));
        var taken = new ConcurrentBag<Guid>();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(worker => Task.Run(async () =>
        {
            while (true)
            {
                var jobs = await ClaimAsync($"worker-{worker}", now, max: 5);
                if (jobs.Count == 0)
                {
                    return;
                }

                foreach (var job in jobs)
                {
                    taken.Add(job.ApplicationId);
                }
            }
        })));

        Assert.Equal(seeded.Count, taken.Count);
        Assert.Equal(seeded.Count, taken.Distinct().Count());
        Assert.Empty(seeded.Except(taken));
    }

    [SqlFact]
    public async Task A_job_stays_held_until_its_lease_runs_out_and_then_goes_to_another_worker()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(3, now.AddMinutes(-1));

        var first = await ClaimAsync("worker-a", now);
        var whileHeld = await ClaimAsync("worker-b", now.AddMinutes(30));
        var afterCrash = await ClaimAsync("worker-b", now.AddHours(2));

        Assert.Equal(3, first.Count);
        Assert.Empty(whileHeld);
        Assert.Equal(3, afterCrash.Count);
    }

    [SqlFact]
    public async Task Jobs_that_are_not_yet_due_are_left_alone()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(2, now.AddMinutes(5));

        Assert.Empty(await ClaimAsync("worker-a", now));
        Assert.Equal(2, (await ClaimAsync("worker-a", now.AddMinutes(6))).Count);
    }

    [SqlFact]
    public async Task A_claim_takes_no_more_than_the_batch_size()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(5, now.AddMinutes(-1));

        var jobs = await ClaimAsync("worker-a", now, max: 2);

        Assert.Equal(2, jobs.Count);
    }

    [SqlFact]
    public async Task A_rescheduled_job_is_released_counted_and_due_later()
    {
        var now = DateTime.UtcNow;
        var id = Assert.Single(await SeedAsync(1, now.AddMinutes(-1)));
        Assert.Single(await ClaimAsync("worker-a", now));

        await using (var database = _Fixture.OpenDatabase("MB"))
        {
            await JobQueue.RescheduleAsync(database, id, 1, now.AddMinutes(1), "retry",
                CancellationToken.None);
        }

        Assert.Empty(await ClaimAsync("worker-b", now));
        var again = Assert.Single(await ClaimAsync("worker-b", now.AddMinutes(2)));
        Assert.Equal(id, again.ApplicationId);
        Assert.Equal(1, again.Attempts);
    }

    [SqlFact]
    public async Task A_completed_job_is_gone()
    {
        var now = DateTime.UtcNow;
        var id = Assert.Single(await SeedAsync(1, now.AddMinutes(-1)));

        await using (var database = _Fixture.OpenDatabase("MB"))
        {
            await JobQueue.CompleteAsync(database, id, CancellationToken.None);
            Assert.False(await database.Jobs.AnyAsync(j => j.ApplicationId == id));
        }
    }

    [SqlFact]
    public async Task Queueing_an_application_twice_leaves_one_job()
    {
        var id = Guid.NewGuid();
        await using var database = _Fixture.OpenDatabase("MB");

        await JobQueue.EnqueueIfMissingAsync(database, id, DateTime.UtcNow, CancellationToken.None);
        await JobQueue.EnqueueIfMissingAsync(database, id, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, await database.Jobs.CountAsync(j => j.ApplicationId == id));
    }
}
