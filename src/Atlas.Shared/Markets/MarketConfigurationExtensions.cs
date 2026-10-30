using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Atlas.Shared.Markets;

/// <summary>
/// Wires the shared market configuration into a service so that every service reads the
/// same single source of truth.
/// </summary>
public static class MarketConfigurationExtensions
{
    /// <summary>The name of the market configuration file copied next to each service.</summary>
    public const string MARKETS_FILE = "markets.json";

    /// <summary>
    /// Adds <c>markets.json</c> from the application's output folder as a configuration source,
    /// with the lowest priority. The file holds the defaults for every market, so a deployment
    /// can still override a single value (a database name, a provider address) with an
    /// environment variable, a setting file or the command line.
    /// </summary>
    /// <param name="configuration">The configuration being built.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IConfigurationBuilder AddMarketsFile(this IConfigurationBuilder configuration)
    {
        var source = new JsonConfigurationSource
        {
            FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
            Path = MARKETS_FILE,
            Optional = false,
            ReloadOnChange = false
        };
        source.ResolveFileProvider();
        configuration.Sources.Insert(0, source);
        return configuration;
    }

    /// <summary>
    /// Registers the validated <see cref="MarketRegistry" /> from the "Markets" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration containing the "Markets" section.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The market configuration is invalid.</exception>
    public static IServiceCollection AddMarkets(this IServiceCollection services,
        IConfiguration configuration)
    {
        var markets = configuration.GetSection("Markets").Get<Dictionary<string, MarketProfile>>()
                      ?? new Dictionary<string, MarketProfile>();
        return services.AddSingleton(new MarketRegistry(markets));
    }
}
