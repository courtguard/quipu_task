using Atlas.Onboarding.Domain.Tests.Support;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Tests.Verification;

public class VerificationDeciderTests
{
    private static readonly MarketProfile _Market =
        TestMarkets.Valid(m => m.IdNowMinConfidence = 0.85);

    private static readonly IdentityCheck _Passed = new(DocumentResult.Valid, true, 0.97);
    private static readonly ScreeningCheck _Clear = new(ScreeningStatus.Clear);
    private static readonly ScreeningCheck _Match = new(ScreeningStatus.PossibleMatch);

    private static VerificationDecision Decide(IdentityCheck? identity, ScreeningCheck? screening)
        => VerificationDecider.Decide(_Market, identity, screening);

    [Fact]
    public void Clears_an_applicant_who_passes_both_checks()
    {
        Assert.Equal(DecisionKind.Clear, Decide(_Passed, _Clear).Kind);
    }

    [Fact]
    public void A_possible_match_goes_to_review_and_is_never_decided_by_the_system()
    {
        var decision = Decide(_Passed, _Match);

        Assert.Equal(DecisionKind.Review, decision.Kind);
        Assert.Null(decision.Reason);
    }

    [Theory]
    [InlineData(DocumentResult.Valid, true, 0.97, null)]
    [InlineData(DocumentResult.Invalid, true, 0.99, RejectionReasons.DOCUMENT_INVALID)]
    [InlineData(DocumentResult.Valid, false, 0.99, RejectionReasons.FACE_MISMATCH)]
    [InlineData(DocumentResult.Inconclusive, true, 0.99,
        RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY)]
    [InlineData(DocumentResult.Valid, true, 0.50, RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY)]
    public void Maps_each_identity_outcome_to_a_decision(DocumentResult document, bool face,
        double confidence, string? expectedReason)
    {
        var decision = Decide(new IdentityCheck(document, face, confidence), _Clear);

        Assert.Equal(expectedReason is null ? DecisionKind.Clear : DecisionKind.Reject,
            decision.Kind);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Fact]
    public void Confidence_exactly_at_the_threshold_passes()
    {
        var decision = Decide(new IdentityCheck(DocumentResult.Valid, true, 0.85), _Clear);

        Assert.Equal(DecisionKind.Clear, decision.Kind);
    }

    [Fact]
    public void Confidence_just_below_the_threshold_asks_the_applicant_to_retry()
    {
        var decision = Decide(new IdentityCheck(DocumentResult.Valid, true, 0.8499), _Clear);

        Assert.Equal(RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY, decision.Reason);
    }

    [Fact]
    public void An_unusable_confidence_value_is_treated_as_too_low()
    {
        var decision = Decide(new IdentityCheck(DocumentResult.Valid, true, double.NaN), _Clear);

        Assert.Equal(RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY, decision.Reason);
    }

    [Fact]
    public void The_threshold_comes_from_the_market()
    {
        var strict = TestMarkets.Valid(m => m.IdNowMinConfidence = 0.99);

        var decision = VerificationDecider.Decide(strict, _Passed, _Clear);

        Assert.Equal(DecisionKind.Reject, decision.Kind);
    }

    [Fact]
    public void A_definite_negative_outranks_a_retryable_one()
    {
        var invalidAndMismatch = new IdentityCheck(DocumentResult.Invalid, false, 0.10);
        var inconclusiveAndMismatch = new IdentityCheck(DocumentResult.Inconclusive, false, 0.10);

        Assert.Equal(RejectionReasons.DOCUMENT_INVALID,
            Decide(invalidAndMismatch, _Clear).Reason);
        Assert.Equal(RejectionReasons.FACE_MISMATCH,
            Decide(inconclusiveAndMismatch, _Clear).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_failed_identity_check_rejects_without_waiting_for_screening(bool matched)
    {
        var failed = new IdentityCheck(DocumentResult.Invalid, false, 0.1);
        var screening = matched ? _Match : null;

        Assert.Equal(DecisionKind.Reject, Decide(failed, screening).Kind);
    }

    [Fact]
    public void A_failed_identity_check_outranks_a_possible_match()
    {
        // No account will be opened, so there is nothing for an officer to approve.
        var failed = new IdentityCheck(DocumentResult.Invalid, false, 0.1);

        Assert.Equal(DecisionKind.Reject, Decide(failed, _Match).Kind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Waits_while_a_required_result_is_missing(bool hasIdentity, bool hasMatch)
    {
        var identity = hasIdentity ? _Passed : null;
        var screening = hasMatch ? _Match : null;

        var decision = Decide(identity, screening);

        Assert.Equal(DecisionKind.Wait, decision.Kind);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void A_missing_result_never_produces_a_rejection()
    {
        // A provider outage must not become a rejection of the applicant.
        Assert.NotEqual(DecisionKind.Reject, Decide(null, null).Kind);
        Assert.NotEqual(DecisionKind.Reject, Decide(_Passed, null).Kind);
        Assert.NotEqual(DecisionKind.Reject, Decide(null, _Clear).Kind);
    }
}
