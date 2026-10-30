using Atlas.Onboarding.Domain.Identity;
using Atlas.Onboarding.Domain.Tests.Support;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Tests.Identity;

public class IdentifierValidatorTests
{
    private static readonly MarketRegistry _Real = TestMarkets.LoadReal();
    private readonly IdentifierValidator _Validator = new(Array.Empty<IChecksumAlgorithm>());

    private IdentifierValidationResult Check(string market, string? nationalId,
        string? passport = null)
    {
        return _Validator.Validate(_Real.Find(market)!, nationalId, passport);
    }

    [Theory]
    [InlineData("MA", "0403991450016")]
    [InlineData("MB", "0403991450016")]
    [InlineData("MD", "1234567890")]
    [InlineData("ME", "1234567890")]
    [InlineData("MC", "AB12CD34E")]
    [InlineData("MC", "ab12cd34e")]
    [InlineData("MF", "ANYTHING123")]
    public void Accepts_well_formed_national_ids(string market, string nationalId)
    {
        Assert.True(Check(market, nationalId).IsValid);
    }

    [Theory]
    [InlineData("MB", "040399145001")]
    [InlineData("MB", "04039914500166")]
    [InlineData("MB", "04039914500AB")]
    [InlineData("MA", "1234567890")]
    [InlineData("MD", "123456789")]
    [InlineData("ME", "12345678901")]
    [InlineData("MC", "AB12CD345")]
    [InlineData("MC", "AB12CD34")]
    [InlineData("MC", "AB12CD34EF")]
    public void Rejects_national_ids_with_the_wrong_format(string market, string nationalId)
    {
        var result = Check(market, nationalId);

        Assert.False(result.IsValid);
        Assert.Equal(IdentifierValidator.NATIONAL_ID_INVALID_FORMAT, Assert.Single(result.Errors));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.True(Check("MB", "  0403991450016 ").IsValid);
    }

    [Theory]
    [InlineData("MA")]
    [InlineData("MB")]
    [InlineData("MC")]
    [InlineData("MD")]
    [InlineData("ME")]
    [InlineData("MF")]
    public void Requires_an_identifier_in_every_market(string market)
    {
        Assert.Equal(IdentifierValidator.IDENTIFIER_REQUIRED,
            Assert.Single(Check(market, null).Errors));
        Assert.Equal(IdentifierValidator.IDENTIFIER_REQUIRED,
            Assert.Single(Check(market, "   ").Errors));
    }

    [Fact]
    public void MF_accepts_a_passport_number_when_there_is_no_national_id()
    {
        Assert.True(Check("MF", null, "P1234567").IsValid);
    }

    [Fact]
    public void MF_rejects_a_malformed_passport_number()
    {
        var result = Check("MF", null, "!!");

        Assert.Equal(IdentifierValidator.PASSPORT_NUMBER_INVALID_FORMAT,
            Assert.Single(result.Errors));
    }

    [Fact]
    public void MF_national_id_is_preferred_over_the_passport_number()
    {
        // A malformed passport number is irrelevant when a valid national id is present.
        Assert.True(Check("MF", "ANYTHING123", "!!").IsValid);
    }

    [Fact]
    public void A_passport_number_is_not_a_substitute_in_markets_that_do_not_accept_it()
    {
        var result = Check("MB", null, "P1234567");

        Assert.Equal(IdentifierValidator.IDENTIFIER_REQUIRED, Assert.Single(result.Errors));
    }

    [Fact]
    public void An_oversized_identifier_is_rejected_even_without_a_pattern()
    {
        var result = Check("MF", new string('A', 33));

        Assert.Equal(IdentifierValidator.NATIONAL_ID_INVALID_FORMAT, Assert.Single(result.Errors));
    }

    [Fact]
    public void Identifier_is_optional_when_the_market_does_not_require_one()
    {
        var market = TestMarkets.Valid(m => m.Identifier.Required = false);

        Assert.True(_Validator.Validate(market, null, null).IsValid);
    }

    [Fact]
    public void Error_results_never_echo_the_identifier()
    {
        var result = Check("MB", "SECRET-VALUE");

        Assert.DoesNotContain(result.Errors, e => e.Contains("SECRET"));
    }

    [Theory]
    [InlineData("1230", true)]
    [InlineData("1231", false)]
    public void Applies_the_configured_checksum_algorithm(string nationalId, bool expected)
    {
        var validator = new IdentifierValidator(new IChecksumAlgorithm[] { new LastDigitZero() });
        var market = TestMarkets.Valid(m => m.Identifier.Checksum = "last-digit-zero");

        var result = validator.Validate(market, nationalId, null);

        Assert.Equal(expected, result.IsValid);
        if (!expected)
        {
            Assert.Equal(IdentifierValidator.NATIONAL_ID_INVALID_CHECKSUM,
                Assert.Single(result.Errors));
        }
    }

    [Fact]
    public void Format_is_checked_before_the_checksum()
    {
        var validator = new IdentifierValidator(new IChecksumAlgorithm[] { new LastDigitZero() });
        var market = TestMarkets.Valid(m => m.Identifier.Checksum = "last-digit-zero");

        var result = validator.Validate(market, "12A0", null);

        Assert.Equal(IdentifierValidator.NATIONAL_ID_INVALID_FORMAT, Assert.Single(result.Errors));
    }

    [Fact]
    public void Reports_markets_that_name_an_unknown_checksum()
    {
        var market = TestMarkets.Valid(m => m.Identifier.Checksum = "does-not-exist");
        market.Code = "XX";

        var problems = _Validator.FindConfigurationProblems(new[] { market });

        Assert.Contains("XX", Assert.Single(problems));
    }

    [Fact]
    public void The_shipped_configuration_names_no_unknown_checksums()
    {
        Assert.Empty(_Validator.FindConfigurationProblems(_Real.All));
    }

    // A deliberately artificial algorithm: it only exists to prove the seam works and is not
    // used by any market.
    private sealed class LastDigitZero : IChecksumAlgorithm
    {
        public string Name => "last-digit-zero";

        public bool IsValid(string value) => value.EndsWith('0');
    }
}
