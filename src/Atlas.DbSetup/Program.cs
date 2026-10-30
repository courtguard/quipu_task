using Atlas.Onboarding.Infrastructure.Provisioning;
using Atlas.Shared.Markets;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddMarketsFile()
    .AddEnvironmentVariables("ATLAS_")
    .AddCommandLine(args)
    .Build();

var admin = configuration["Admin:ConnectionString"];
if (string.IsNullOrWhiteSpace(admin))
{
    Console.Error.WriteLine("Admin:ConnectionString is required.");
    return 2;
}

var markets = configuration.GetSection("Markets").Get<Dictionary<string, MarketProfile>>()
              ?? new Dictionary<string, MarketProfile>();
var registry = new MarketRegistry(markets);
var onboarding = ReadLogin("Onboarding");
var review = ReadLogin("Review");

try
{
    foreach (var market in registry.All.OrderBy(m => m.Code))
    {
        Console.Write($"{market.Code}: database {market.Database} ... ");
        await MarketDatabaseProvisioner.ProvisionAsync(admin, market, onboarding, review,
            CancellationToken.None);
        Console.WriteLine("ready");
    }

    Describe(onboarding, "the onboarding tables, and may only add to the access log");
    Describe(review, "the review queue and decisions, and may only add to the access log");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Setup failed: {ex.Message}");
    Console.Error.WriteLine("Is SQL Server running (docker compose up -d in /platform)?");
    return 1;
}

ServiceLogin? ReadLogin(string service)
{
    var name = configuration[$"Logins:{service}:Name"];
    var password = configuration[$"Logins:{service}:Password"];
    return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password)
        ? null
        : new ServiceLogin(name, password);
}

static void Describe(ServiceLogin? login, string access)
{
    Console.WriteLine(login is null
        ? "A service login is not configured, so it was not created."
        : $"Login '{login.Name}' can use {access}.");
}
