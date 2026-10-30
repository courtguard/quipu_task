namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// The application statuses exposed to the mobile app. They deliberately say less than the
/// internal status: nothing here reveals that screening found a possible sanctions match.
/// </summary>
public static class PublicApplicationStatus
{
    /// <summary>The application was approved; the account is open and the card ordered.</summary>
    public const string APPROVED = "APPROVED";

    /// <summary>The application was declined.</summary>
    public const string REJECTED = "REJECTED";

    /// <summary>The application is still being worked on; no action is needed.</summary>
    public const string PROCESSING = "PROCESSING";

    /// <summary>The application is being reviewed and may take up to two days.</summary>
    public const string UNDER_REVIEW = "UNDER_REVIEW";

    /// <summary>The applicant must visit a branch to activate the account.</summary>
    public const string AWAITING_BRANCH_ACTIVATION = "AWAITING_BRANCH_ACTIVATION";

    /// <summary>The application could not be completed in time; the applicant may reapply.</summary>
    public const string EXPIRED = "EXPIRED";

    /// <summary>Maps an internal status to the one shown to the customer.</summary>
    /// <param name="status">The internal status.</param>
    /// <returns>The public status.</returns>
    public static string From(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.Processing => PROCESSING,
            ApplicationStatus.Fulfilling => PROCESSING,
            ApplicationStatus.PendingComplianceReview => UNDER_REVIEW,
            ApplicationStatus.AwaitingBranchActivation => AWAITING_BRANCH_ACTIVATION,
            ApplicationStatus.Approved => APPROVED,
            ApplicationStatus.Rejected => REJECTED,
            ApplicationStatus.Expired => EXPIRED,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }
}
