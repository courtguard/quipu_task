using Atlas.Shared.Markets;
using Microsoft.Extensions.Configuration;

namespace Atlas.Onboarding.Domain.Tests.Support;

/// <summary>Builds market registries for tests.</summary>
internal static class TestMarkets
{
    /// <summary>Loads the real shipped <c>markets.json</c>.</summary>
    public static MarketRegistry LoadReal()
    {
        var configuration = new ConfigurationBuilder().AddMarketsFile().Build();
        var markets = configuration.GetSection("Markets")
            .Get<Dictionary<string, MarketProfile>>()!;
        return new MarketRegistry(markets);
    }

    /// <summary>A valid market that tests can adjust, to test one rule at a time.</summary>
    public static MarketProfile Valid(Action<MarketProfile>? adjust = null)
    {
        var profile = new MarketProfile
        {
            Enabled = true,
            IdNowMinConfidence = 0.85,
            Database = "Atlas_XX",
            BlobContainer = "xx-documents",
            IdNowBaseUrl = "http://localhost:5300/idnow/",
            WorldCheckBaseUrl = "http://localhost:5300/worldcheck/",
            Identifier = new IdentifierRules { NationalIdPattern = "^[0-9]{4}$" }
        };
        adjust?.Invoke(profile);
        return profile;
    }

    /// <summary>A registry holding only the given market, under code "XX".</summary>
    public static MarketRegistry Registry(MarketProfile profile)
    {
        return new MarketRegistry(new Dictionary<string, MarketProfile> { ["XX"] = profile });
    }
}
