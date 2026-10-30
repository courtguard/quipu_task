using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Tests.Support;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Tests.Applications;

public class ApplicationTests
{
    private static readonly DateTime _Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IdentityCheck _Passed = new(DocumentResult.Valid, true, 0.97);
    private static readonly MarketProfile _Remote = TestMarkets.Valid();
    private static readonly MarketProfile _InPerson =
        TestMarkets.Valid(m => m.ActivationMode = ActivationMode.InPerson);

    private static Application NewApplication()
    {
        var applicant = new ApplicantDetails("Ana", "Petrova", new DateOnly(1991, 3, 4), "MB",
            "0403991450016", null, "ana@example.com", "+10000000");
        return Application.Submit(Guid.NewGuid(), "MB", applicant, new byte[] { 1 }, "key-1",
            new byte[] { 2 }, _Now);
    }

    private static Application Fulfilling()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_1", _Now);
        application.ApplyVerification(_Remote, _Now);
        return application;
    }

    [Fact]
    public void A_new_application_is_processing_and_keeps_its_details()
    {
        var application = NewApplication();

        Assert.Equal(ApplicationStatus.Processing, application.Status);
        Assert.Equal("MB", application.Market);
        Assert.Equal("Ana Petrova", application.Applicant.FullName);
        Assert.Null(application.Identity);
        Assert.Null(application.Screening);
        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void The_retention_period_is_ten_years_from_the_date_of_application()
    {
        var application = NewApplication();

        Assert.Equal(_Now.AddYears(10), application.RetentionUntilUtc);
    }

    [Fact]
    public void The_record_id_is_independent_of_the_applicant_identifier()
    {
        var first = NewApplication();
        var second = NewApplication();

        Assert.NotEqual(first.Id, second.Id);
        Assert.DoesNotContain(first.NationalId!, first.Id.ToString());
    }

    [Fact]
    public void Waits_until_both_results_are_in()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);

        var changed = application.ApplyVerification(_Remote, _Now);

        Assert.False(changed);
        Assert.Equal(ApplicationStatus.Processing, application.Status);
    }

    [Fact]
    public void Clears_a_remote_applicant_into_fulfilment()
    {
        var application = Fulfilling();

        Assert.Equal(ApplicationStatus.Fulfilling, application.Status);
    }

    [Fact]
    public void Clears_an_in_person_applicant_into_awaiting_the_branch_visit()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_1", _Now);

        application.ApplyVerification(_InPerson, _Now);

        Assert.Equal(ApplicationStatus.AwaitingBranchActivation, application.Status);
    }

    [Fact]
    public void A_possible_match_waits_for_a_compliance_officer()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.PossibleMatch), "wc_1",
            _Now);

        application.ApplyVerification(_Remote, _Now);

        Assert.Equal(ApplicationStatus.PendingComplianceReview, application.Status);
        Assert.Null(application.AccountId);
    }

    [Fact]
    public void A_failed_identity_check_rejects_with_a_reason()
    {
        var application = NewApplication();
        application.RecordIdentity(new IdentityCheck(DocumentResult.Inconclusive, true, 0.5),
            "idn_1", _Now);

        application.ApplyVerification(_Remote, _Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Equal(RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY, application.RejectionReason);
    }

    [Fact]
    public void A_result_cannot_be_recorded_twice()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_1", _Now);

        Assert.Throws<InvalidOperationException>(
            () => application.RecordIdentity(_Passed, "idn_2", _Now));
        Assert.Throws<InvalidOperationException>(
            () => application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_2",
                _Now));
    }

    [Fact]
    public void Results_cannot_be_recorded_once_the_application_is_decided()
    {
        var application = Fulfilling();

        Assert.Throws<InvalidOperationException>(
            () => application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_2",
                _Now));
    }

    [Fact]
    public void The_card_cannot_be_ordered_before_the_account()
    {
        var application = Fulfilling();

        Assert.Throws<InvalidOperationException>(
            () => application.RecordCardOrder("crd_1", _Now));
    }

    [Fact]
    public void Cannot_be_approved_with_only_an_account()
    {
        var application = Fulfilling();
        application.RecordAccount("acc_1", _Now);

        Assert.Throws<InvalidOperationException>(() => application.Approve(_Now));
        Assert.Equal(ApplicationStatus.Fulfilling, application.Status);
    }

    [Fact]
    public void Is_approved_once_the_account_is_open_and_the_card_is_ordered()
    {
        var application = Fulfilling();
        application.RecordAccount("acc_1", _Now);
        application.RecordCardOrder("crd_1", _Now);

        application.Approve(_Now);

        Assert.Equal(ApplicationStatus.Approved, application.Status);
    }

    [Fact]
    public void An_approved_application_cannot_change()
    {
        var application = Fulfilling();
        application.RecordAccount("acc_1", _Now);
        application.RecordCardOrder("crd_1", _Now);
        application.Approve(_Now);

        Assert.Throws<InvalidOperationException>(() => application.Expire(_Now));
        Assert.Throws<InvalidOperationException>(() => application.ActivateAtBranch(_Now));
    }

    [Fact]
    public void A_branch_visit_moves_the_application_into_fulfilment()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.Clear), "wc_1", _Now);
        application.ApplyVerification(_InPerson, _Now);

        application.ActivateAtBranch(_Now);

        Assert.Equal(ApplicationStatus.Fulfilling, application.Status);
    }

    [Fact]
    public void The_branch_activation_is_refused_for_anyone_not_waiting_for_it()
    {
        Assert.Throws<InvalidOperationException>(() => NewApplication().ActivateAtBranch(_Now));
    }

    [Fact]
    public void Expires_only_while_processing_and_leaves_no_rejection_reason()
    {
        var application = NewApplication();

        application.Expire(_Now);

        Assert.Equal(ApplicationStatus.Expired, application.Status);
        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void A_review_cannot_expire()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.PossibleMatch), "wc_1",
            _Now);
        application.ApplyVerification(_Remote, _Now);

        Assert.Throws<InvalidOperationException>(() => application.Expire(_Now));
    }

    private static Application UnderReview()
    {
        var application = NewApplication();
        application.RecordIdentity(_Passed, "idn_1", _Now);
        application.RecordScreening(new ScreeningCheck(ScreeningStatus.PossibleMatch), "wc_1",
            _Now);
        application.ApplyVerification(_Remote, _Now);
        return application;
    }

    [Fact]
    public void An_officer_can_clear_a_reviewed_application_into_fulfilment()
    {
        var application = UnderReview();

        application.ClearAfterReview(_Remote, _Now);

        Assert.Equal(ApplicationStatus.Fulfilling, application.Status);
    }

    [Fact]
    public void A_cleared_review_in_an_in_person_market_still_needs_the_branch_visit()
    {
        var application = UnderReview();

        application.ClearAfterReview(_InPerson, _Now);

        Assert.Equal(ApplicationStatus.AwaitingBranchActivation, application.Status);
    }

    [Fact]
    public void An_officer_can_decline_with_a_reason_that_reveals_nothing()
    {
        var application = UnderReview();

        application.RejectAfterReview(_Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Equal(RejectionReasons.DECLINED, application.RejectionReason);
    }

    [Fact]
    public void Only_an_application_under_review_can_be_decided_by_an_officer()
    {
        Assert.Throws<InvalidOperationException>(
            () => NewApplication().ClearAfterReview(_Remote, _Now));
        Assert.Throws<InvalidOperationException>(() => Fulfilling().RejectAfterReview(_Now));
    }

    [Fact]
    public void An_erasure_request_restricts_the_record_whatever_its_status()
    {
        foreach (var application in new[] { NewApplication(), UnderReview(), Fulfilling() })
        {
            var status = application.Status;

            application.RestrictForErasure(_Now);

            Assert.True(application.IsRestricted);
            Assert.Equal(_Now, application.ErasureRequestedAtUtc);
            Assert.Equal(status, application.Status);
        }
    }

    [Fact]
    public void Asking_for_erasure_twice_keeps_the_first_request()
    {
        var application = NewApplication();
        application.RestrictForErasure(_Now);

        application.RestrictForErasure(_Now.AddDays(3));

        Assert.Equal(_Now, application.ErasureRequestedAtUtc);
    }

    [Fact]
    public void An_erasure_request_does_not_shorten_the_retention_period()
    {
        var application = NewApplication();

        application.RestrictForErasure(_Now);

        Assert.Equal(_Now.AddYears(10), application.RetentionUntilUtc);
        Assert.False(application.IsDueForDeletion(_Now.AddYears(10).AddDays(-1)));
        Assert.True(application.IsDueForDeletion(_Now.AddYears(10)));
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(48, true)]
    public void Outlives_the_maximum_processing_age(int hours, bool expected)
    {
        var application = NewApplication();

        var outlived = application.HasOutlived(_Now.AddHours(hours), TimeSpan.FromHours(24));

        Assert.Equal(expected, outlived);
    }

    [Fact]
    public void Only_a_processing_application_can_outlive_its_age()
    {
        var application = Fulfilling();

        Assert.False(application.HasOutlived(_Now.AddYears(1), TimeSpan.FromHours(24)));
    }

    [Fact]
    public void Updates_the_modification_time_on_every_change()
    {
        var application = NewApplication();
        var later = _Now.AddMinutes(5);

        application.RecordIdentity(_Passed, "idn_1", later);

        Assert.Equal(later, application.UpdatedAtUtc);
        Assert.Equal(_Now, application.CreatedAtUtc);
    }
}
