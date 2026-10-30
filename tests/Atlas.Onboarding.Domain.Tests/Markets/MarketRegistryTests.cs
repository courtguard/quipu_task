using Atlas.Onboarding.Domain.Tests.Support;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Tests.Markets;

public class MarketRegistryTests
{
    private static readonly MarketRegistry _Real = TestMarkets.LoadReal();

    [Fact]
    public void The_shipped_configuration_defines_the_six_markets()
    {
        var codes = _Real.All.Select(m => m.Code).OrderBy(c => c);

        Assert.Equal(new[] { "MA", "MB", "MC", "MD", "ME", "MF" }, codes);
    }

    [Fact]
    public void Only_MD_requires_in_person_activation()
    {
        var inPerson = _Real.All.Where(m => m.ActivationMode == ActivationMode.InPerson);

        Assert.Equal("MD", Assert.Single(inPerson).Code);
    }

    [Fact]
    public void Only_MF_accepts_a_passport_in_lieu_of_a_national_id()
    {
        var accepting = _Real.All.Where(m => m.Identifier.PassportAcceptedInLieu);

        Assert.Equal("MF", Assert.Single(accepting).Code);
    }

    [Fact]
    public void Every_market_has_its_own_database_and_blob_container()
    {
        // Personal data must not leave the customer's country: no two markets may share a store.
        Assert.Equal(_Real.All.Count, _Real.All.Select(m => m.Database).Distinct().Count());
        Assert.Equal(_Real.All.Count, _Real.All.Select(m => m.BlobContainer).Distinct().Count());
    }

    [Fact]
    public void Explicit_nulls_in_the_configuration_mean_not_set()
    {
        // MF has no national id pattern and no market has a checksum; the JSON says null.
        Assert.Null(_Real.Find("MF")!.Identifier.NationalIdPattern);
        Assert.All(_Real.All, m => Assert.Null(m.Identifier.Checksum));
    }

    [Theory]
    [InlineData("mb")]
    [InlineData("MB")]
    [InlineData(" MB ")]
    public void Finds_markets_ignoring_case_and_padding(string code)
    {
        Assert.Equal("MB", _Real.Find(code)?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ZZ")]
    public void Unknown_markets_are_not_found(string? code)
    {
        Assert.Null(_Real.Find(code));
    }

    [Fact]
    public void Rejects_an_empty_configuration()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new MarketRegistry(new Dictionary<string, MarketProfile>()));

        Assert.Contains("no markets", ex.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void Rejects_a_confidence_threshold_outside_0_to_1(double threshold)
    {
        var market = TestMarkets.Valid(m => m.IdNowMinConfidence = threshold);

        var ex = Assert.Throws<InvalidOperationException>(() => TestMarkets.Registry(market));

        Assert.Contains("XX: IdNowMinConfidence", ex.Message);
    }

    [Fact]
    public void Rejects_a_relative_provider_url()
    {
        var market = TestMarkets.Valid(m => m.IdNowBaseUrl = "idnow/");

        Assert.Throws<InvalidOperationException>(() => TestMarkets.Registry(market));
    }

    [Fact]
    public void Rejects_an_invalid_regular_expression()
    {
        var market = TestMarkets.Valid(m => m.Identifier.NationalIdPattern = "([0-9");

        var ex = Assert.Throws<InvalidOperationException>(() => TestMarkets.Registry(market));

        Assert.Contains("not a valid regular expression", ex.Message);
    }

    [Fact]
    public void Rejects_passports_in_lieu_without_a_passport_pattern()
    {
        var market = TestMarkets.Valid(m => m.Identifier.PassportAcceptedInLieu = true);

        Assert.Throws<InvalidOperationException>(() => TestMarkets.Registry(market));
    }

    [Fact]
    public void Reports_every_problem_not_just_the_first()
    {
        var market = TestMarkets.Valid(m =>
        {
            m.Database = "";
            m.BlobContainer = "";
        });

        var ex = Assert.Throws<InvalidOperationException>(() => TestMarkets.Registry(market));

        Assert.Contains("Database is required", ex.Message);
        Assert.Contains("BlobContainer is required", ex.Message);
    }
}
