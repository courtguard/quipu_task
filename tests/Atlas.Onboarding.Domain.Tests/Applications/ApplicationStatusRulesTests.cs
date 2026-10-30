using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Tests.Support;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Tests.Applications;

public class ApplicationStatusRulesTests
{
    private static readonly ApplicationStatus[] _All = Enum.GetValues<ApplicationStatus>();

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Expired)]
    public void Final_statuses_cannot_change(ApplicationStatus final)
    {
        Assert.True(ApplicationStatusRules.IsFinal(final));
        Assert.All(_All, to => Assert.False(ApplicationStatusRules.CanTransition(final, to)));
    }

    [Theory]
    [InlineData(ApplicationStatus.Processing)]
    [InlineData(ApplicationStatus.PendingComplianceReview)]
    [InlineData(ApplicationStatus.AwaitingBranchActivation)]
    [InlineData(ApplicationStatus.Fulfilling)]
    public void Open_statuses_are_not_final(ApplicationStatus open)
    {
        Assert.False(ApplicationStatusRules.IsFinal(open));
    }

    [Fact]
    public void No_status_can_move_to_itself()
    {
        Assert.All(_All, s => Assert.False(ApplicationStatusRules.CanTransition(s, s)));
    }

    [Fact]
    public void Nothing_can_move_back_to_processing()
    {
        Assert.All(_All, s =>
            Assert.False(ApplicationStatusRules.CanTransition(s, ApplicationStatus.Processing)));
    }

    [Theory]
    [InlineData(ApplicationStatus.Processing, ApplicationStatus.PendingComplianceReview)]
    [InlineData(ApplicationStatus.Processing, ApplicationStatus.Fulfilling)]
    [InlineData(ApplicationStatus.Processing, ApplicationStatus.AwaitingBranchActivation)]
    [InlineData(ApplicationStatus.Processing, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Processing, ApplicationStatus.Expired)]
    [InlineData(ApplicationStatus.PendingComplianceReview, ApplicationStatus.Fulfilling)]
    [InlineData(ApplicationStatus.PendingComplianceReview,
        ApplicationStatus.AwaitingBranchActivation)]
    [InlineData(ApplicationStatus.PendingComplianceReview, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.AwaitingBranchActivation, ApplicationStatus.Fulfilling)]
    [InlineData(ApplicationStatus.Fulfilling, ApplicationStatus.Approved)]
    public void Allows_the_intended_transitions(ApplicationStatus from, ApplicationStatus to)
    {
        Assert.True(ApplicationStatusRules.CanTransition(from, to));
    }

    [Fact]
    public void A_review_can_only_be_decided_by_an_officer_and_does_not_expire()
    {
        // Compliance review may take up to 48 hours; the technical expiry must not cancel it.
        Assert.False(ApplicationStatusRules.CanTransition(
            ApplicationStatus.PendingComplianceReview, ApplicationStatus.Expired));
        Assert.False(ApplicationStatusRules.CanTransition(
            ApplicationStatus.PendingComplianceReview, ApplicationStatus.Approved));
    }

    [Fact]
    public void Nothing_reaches_approved_except_through_fulfilment()
    {
        var sources = _All.Where(s => ApplicationStatusRules.CanTransition(s,
            ApplicationStatus.Approved));

        Assert.Equal(ApplicationStatus.Fulfilling, Assert.Single(sources));
    }

    [Theory]
    [InlineData(ApplicationStatus.Processing, true)]
    [InlineData(ApplicationStatus.PendingComplianceReview, true)]
    [InlineData(ApplicationStatus.AwaitingBranchActivation, true)]
    [InlineData(ApplicationStatus.Fulfilling, true)]
    [InlineData(ApplicationStatus.Approved, true)]
    [InlineData(ApplicationStatus.Rejected, false)]
    [InlineData(ApplicationStatus.Expired, false)]
    public void Only_rejected_and_expired_applicants_may_apply_again(ApplicationStatus status,
        bool blocks)
    {
        Assert.Equal(blocks, ApplicationStatusRules.BlocksNewApplication(status));
    }

    [Fact]
    public void Remote_markets_go_straight_to_fulfilment()
    {
        var market = TestMarkets.Valid(m => m.ActivationMode = ActivationMode.Remote);

        Assert.Equal(ApplicationStatus.Fulfilling,
            ApplicationStatusRules.NextAfterClearance(market));
    }

    [Fact]
    public void In_person_markets_wait_for_the_branch_visit()
    {
        var market = TestMarkets.Valid(m => m.ActivationMode = ActivationMode.InPerson);

        Assert.Equal(ApplicationStatus.AwaitingBranchActivation,
            ApplicationStatusRules.NextAfterClearance(market));
    }

    [Fact]
    public void Every_status_has_a_public_status()
    {
        Assert.All(_All, s => Assert.False(string.IsNullOrEmpty(PublicApplicationStatus.From(s))));
    }

    [Fact]
    public void The_public_status_never_reveals_that_screening_found_a_match()
    {
        var shown = PublicApplicationStatus.From(ApplicationStatus.PendingComplianceReview);

        Assert.Equal(PublicApplicationStatus.UNDER_REVIEW, shown);
        Assert.DoesNotContain("SANCTION", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PEP", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATCH", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fulfilment_is_shown_as_processing()
    {
        Assert.Equal(PublicApplicationStatus.PROCESSING,
            PublicApplicationStatus.From(ApplicationStatus.Fulfilling));
    }
}
