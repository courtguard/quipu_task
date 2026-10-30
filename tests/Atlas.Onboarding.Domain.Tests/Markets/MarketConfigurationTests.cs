using Atlas.Shared.Markets;
using Microsoft.Extensions.Configuration;

namespace Atlas.Onboarding.Domain.Tests.Markets;

public class MarketConfigurationTests
{
    [Fact]
    public void Other_configuration_overrides_the_markets_file()
    {
        // A deployment must be able to change one market's value without editing the file.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Markets:MB:Database"] = "Overridden_MB"
            })
            .AddMarketsFile()
            .Build();

        Assert.Equal("Overridden_MB", configuration["Markets:MB:Database"]);
        Assert.Equal("Atlas_MA", configuration["Markets:MA:Database"]);
    }

    [Fact]
    public void The_markets_file_provides_the_defaults()
    {
        var configuration = new ConfigurationBuilder().AddMarketsFile().Build();

        Assert.Equal("Atlas_MB", configuration["Markets:MB:Database"]);
        Assert.Equal("InPerson", configuration["Markets:MD:ActivationMode"]);
    }
}
