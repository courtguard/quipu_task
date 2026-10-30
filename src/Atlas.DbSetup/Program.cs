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

var loginName = configuration["Logins:Onboarding:Name"];
var loginPassword = configuration["Logins:Onboarding:Password"];
var login = string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(loginPassword)
    ? null
    : new ServiceLogin(loginName, loginPassword);

try
{
    foreach (var market in registry.All.OrderBy(m => m.Code))
    {
        Console.Write($"{market.Code}: database {market.Database} ... ");
        await MarketDatabaseProvisioner.ProvisionAsync(admin, market, login,
            CancellationToken.None);
        Console.WriteLine("ready");
    }

    Console.WriteLine(login is null
        ? "No service login configured, so none was created."
        : $"Service login '{login.Name}' has access to the onboarding tables only.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Setup failed: {ex.Message}");
    Console.Error.WriteLine("Is SQL Server running (docker compose up -d in /platform)?");
    return 1;
}
