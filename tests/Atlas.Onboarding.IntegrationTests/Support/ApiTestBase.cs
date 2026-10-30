using Atlas.Onboarding.Api;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>
/// A test that talks to its own running API. Each test gets a fresh API with its own worker, and
/// the worker is stopped and the queue emptied afterwards, so tests cannot influence each other.
/// </summary>
[Collection("atlas")]
public abstract class ApiTestBase : IDisposable
{
    private readonly WebApplicationFactory<ApiMarker>? _Factory;

    /// <summary>Starts the API, with some settings changed.</summary>
    protected ApiTestBase(AtlasFixture fixture,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        Fixture = fixture;
        if (TestSql.IsAvailable)
        {
            _Factory = fixture.CreateApi(settings);
            Client = _Factory.CreateClient();
        }
    }

    /// <summary>The shared databases and stand-ins.</summary>
    protected AtlasFixture Fixture { get; }

    /// <summary>A client of the API.</summary>
    protected HttpClient Client { get; } = null!;

    /// <summary>Stops the API and its worker, then empties the queue.</summary>
    public void Dispose()
    {
        Client?.Dispose();
        _Factory?.Dispose();
        if (TestSql.IsAvailable)
        {
            Fixture.PurgeJobs();
        }
    }

    /// <summary>Reads an application back from its market's database.</summary>
    protected async Task<Application> LoadAsync(string publicId)
    {
        var parsed = ApplicationIds.Parse(publicId)!.Value;
        await using var database = Fixture.OpenDatabase(parsed.Market);
        return await database.Applications.Include(a => a.Documents)
            .SingleAsync(a => a.Id == parsed.Id);
    }

    /// <summary>Whether the application still has work queued.</summary>
    protected async Task<bool> HasJobAsync(string publicId)
    {
        var parsed = ApplicationIds.Parse(publicId)!.Value;
        await using var database = Fixture.OpenDatabase(parsed.Market);
        return await database.Jobs.AnyAsync(j => j.ApplicationId == parsed.Id);
    }

    /// <summary>Waits for a condition, polling, and fails the test if it never holds.</summary>
    protected static async Task EventuallyAsync(Func<Task<bool>> condition, string because,
        int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for: " + because);
    }
}
