using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Atlas.Onboarding.Api;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Review.Api;
using Atlas.Shared.Applications;
using Atlas.Onboarding.Infrastructure.Provisioning;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Shared.Markets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>
/// Starts everything once for all the tests: a fresh database per market, the stand-in providers,
/// and the Onboarding API with its worker wired to both.
/// </summary>
public sealed class AtlasFixture : IAsyncLifetime
{
    private const string KEY_A = "nkXaqfKvQx4b1Ua4ibefsPLgzhGLWC1pnpsAYkwEt/wL/7IWnAeaq7g8KM6Bnz8f";
    private const string KEY_B = "kZI5Hl4V7obBsFCHCI7HEfkzrM8LiXKMPqRXCu/hFgrtzyeY8VqVDSLS36Zq8pij";
    private const string IDP_KEY =
        "UcmXcyBo6wFBC8oj02ZpSKqjhrGJD+SDKNMBJJVdlVZghQGyWTVQeSMUt63YwLse";

    private readonly string _Run = Guid.NewGuid().ToString("N")[..8];
    private readonly List<IDisposable> _Disposables = new();
    private WebApplicationFactory<Program>? _Stubs;

    /// <summary>The six market codes.</summary>
    public static readonly string[] MarketCodes = { "MA", "MB", "MC", "MD", "ME", "MF" };

    /// <summary>Where documents were stored.</summary>
    public InMemoryDocumentStore Documents { get; } = new();

    /// <summary>The databases created for this run, by market code.</summary>
    public IReadOnlyDictionary<string, string> Databases { get; } = MarketCodes
        .ToDictionary(c => c, c => $"AtlasTest_{Guid.NewGuid().ToString("N")[..8]}_{c}");

    /// <summary>The stand-in providers.</summary>
    public WebApplicationFactory<Program> Stubs => _Stubs!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (!TestSql.IsAvailable)
        {
            return;
        }

        var registry = LoadMarkets();
        foreach (var code in MarketCodes)
        {
            var market = registry.Find(code)!;
            market.Database = Databases[code];
            await MarketDatabaseProvisioner.ProvisionAsync(TestSql.AdminConnectionString!, market,
                null, null, CancellationToken.None);
        }

        _Stubs = new WebApplicationFactory<Program>();
        _Disposables.Add(_Stubs);
    }

    /// <summary>Empties every market's job queue, so that a finished test leaves no work.</summary>
    public void PurgeJobs()
    {
        foreach (var code in MarketCodes)
        {
            using var database = OpenDatabase(code);
            database.Database.ExecuteSqlRaw("DELETE FROM dbo.Jobs");
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        foreach (var disposable in _Disposables)
        {
            disposable.Dispose();
        }

        if (!TestSql.IsAvailable)
        {
            return;
        }

        foreach (var database in Databases.Values)
        {
            await MarketDatabaseProvisioner.DropDatabaseAsync(TestSql.AdminConnectionString!,
                database, CancellationToken.None);
        }
    }

    /// <summary>
    /// Starts another API instance over the same databases and stand-ins, with some settings
    /// changed. Instances running side by side behave like replicas.
    /// </summary>
    /// <param name="overrides">Configuration keys to change.</param>
    /// <returns>The running API.</returns>
    public WebApplicationFactory<ApiMarker> CreateApi(
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["Sql:ConnectionString"] = TestSql.AdminConnectionString!,
            ["Secrets:ApplicantHashKey"] = KEY_A,
            ["Secrets:StatusTokenKey"] = KEY_B,
            ["Processing:SyncWaitSeconds"] = "20",
            ["Processing:WorkerPollMilliseconds"] = "50",
            ["Processing:WaitPollMilliseconds"] = "50",
            ["Processing:RetryBaseSeconds"] = "0.05",
            ["Processing:RetryMaxSeconds"] = "0.2",
            ["Seq:Url"] = "http://localhost:1"
        };
        foreach (var code in MarketCodes)
        {
            settings[$"Markets:{code}:Database"] = Databases[code];
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, string>())
        {
            settings[key] = value;
        }

        var factory = new WebApplicationFactory<ApiMarker>().WithWebHostBuilder(host =>
        {
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }

            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDocumentStore>();
                services.AddSingleton<IDocumentStore>(Documents);

                // Calls to IDNow, World-Check and core banking go to the in-process stand-ins.
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(builder =>
                        builder.PrimaryHandler = Stubs.Server.CreateHandler()));
            });
        });
        _Disposables.Add(factory);
        return factory;
    }

    /// <summary>Starts the Review API over this run's databases.</summary>
    /// <param name="overrides">Configuration keys to change.</param>
    /// <returns>The running API.</returns>
    public WebApplicationFactory<ReviewApiMarker> CreateReviewApi(
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["Sql:ConnectionString"] = TestSql.AdminConnectionString!,
            ["Auth:SigningKey"] = IDP_KEY,
            ["Seq:Url"] = "http://localhost:1"
        };
        foreach (var code in MarketCodes)
        {
            settings[$"Markets:{code}:Database"] = Databases[code];
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, string>())
        {
            settings[key] = value;
        }

        var factory = new WebApplicationFactory<ReviewApiMarker>().WithWebHostBuilder(host =>
        {
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
        });
        _Disposables.Add(factory);
        return factory;
    }

    /// <summary>Signs a member of staff in at the stand-in identity provider.</summary>
    /// <param name="subject">Their identifier.</param>
    /// <param name="role">compliance_officer or branch_staff.</param>
    /// <param name="markets">The markets they work for.</param>
    /// <param name="lifetimeMinutes">How long the token lasts; negative for an expired one.</param>
    /// <returns>The bearer token.</returns>
    public async Task<string> StaffTokenAsync(string subject, string role, string[] markets,
        int lifetimeMinutes = 60)
    {
        using var client = Stubs.CreateClient();
        var response = await client.PostAsJsonAsync("/idp/token", new
        {
            subject,
            name = subject,
            role,
            markets,
            lifetimeMinutes
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return (string)body!["accessToken"]!;
    }

    /// <summary>Opens the database of one market, to inspect what was stored.</summary>
    /// <param name="marketCode">The market.</param>
    /// <returns>A context the caller disposes.</returns>
    public OnboardingDbContext OpenDatabase(string marketCode)
    {
        var connection = new SqlConnectionStringBuilder(TestSql.AdminConnectionString)
        {
            InitialCatalog = Databases[marketCode]
        }.ConnectionString;
        return new OnboardingDbContext(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlServer(connection).Options);
    }

    /// <summary>Reads the access log of an application, as (actor, action) pairs.</summary>
    /// <param name="publicId">The application's public identifier.</param>
    /// <param name="actor">Only this actor's entries, or null for all.</param>
    /// <returns>The entries, oldest first.</returns>
    public async Task<List<(string Actor, string Action)>> AccessLogAsync(string publicId,
        string? actor = null)
    {
        var parsed = ApplicationIds.Parse(publicId)!.Value;
        await using var database = OpenDatabase(parsed.Market);
        var entries = await database.AccessLog
            .Where(e => e.ApplicationId == parsed.Id && (actor == null || e.Actor == actor))
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new { e.Actor, e.Action })
            .ToListAsync();
        return entries.Select(e => (e.Actor, e.Action)).ToList();
    }

    /// <summary>Reads the staff decision recorded for an application.</summary>
    /// <param name="publicId">The application's public identifier.</param>
    /// <returns>The decision.</returns>
    public async Task<StaffDecision> DecisionAsync(string publicId)
    {
        var parsed = ApplicationIds.Parse(publicId)!.Value;
        await using var database = OpenDatabase(parsed.Market);
        return await database.StaffDecisions.SingleAsync(d => d.ApplicationId == parsed.Id);
    }

    /// <summary>The market's profile as the API sees it, with this run's database.</summary>
    /// <param name="marketCode">The market.</param>
    /// <returns>The profile.</returns>
    public MarketProfile Market(string marketCode)
    {
        var market = LoadMarkets().Find(marketCode)!;
        market.Database = Databases[marketCode];
        return market;
    }

    private static MarketRegistry LoadMarkets()
    {
        var configuration = new ConfigurationBuilder().AddMarketsFile().Build();
        var markets = configuration.GetSection("Markets")
            .Get<Dictionary<string, MarketProfile>>()!;
        return new MarketRegistry(markets);
    }
}

/// <summary>Makes every test class share one <see cref="AtlasFixture" />.</summary>
[CollectionDefinition("atlas")]
public sealed class AtlasCollection : ICollectionFixture<AtlasFixture>
{
}
