namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// Where an onboarding application is in its life. This is the internal status; what the
/// customer is told is <see cref="PublicApplicationStatus" />.
/// </summary>
public enum ApplicationStatus
{
    /// <summary>
    /// Identity verification and sanctions screening are running, or waiting for a provider that
    /// is slow or unavailable.
    /// </summary>
    Processing,

    /// <summary>
    /// Screening found a possible sanctions or PEP match. A compliance officer in the applicant's
    /// market must decide; the system never decides this case.
    /// </summary>
    PendingComplianceReview,

    /// <summary>
    /// The applicant is cleared in a market that requires them to attend a branch and sign in
    /// person before the account may be activated.
    /// </summary>
    AwaitingBranchActivation,

    /// <summary>The applicant is cleared and the account and card are being set up.</summary>
    Fulfilling,

    /// <summary>The applicant has an open account and an ordered card.</summary>
    Approved,

    /// <summary>The application was declined. The applicant may apply again.</summary>
    Rejected,

    /// <summary>
    /// The application could not be completed in time, for example because a provider stayed
    /// unavailable. This is not a judgement on the applicant, who may apply again.
    /// </summary>
    Expired
}
