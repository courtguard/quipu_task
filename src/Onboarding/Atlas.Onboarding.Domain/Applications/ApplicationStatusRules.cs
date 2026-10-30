using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// The rules for how an application may move between statuses.
/// </summary>
public static class ApplicationStatusRules
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> _Allowed = new()
    {
        [ApplicationStatus.Processing] = new[]
        {
            ApplicationStatus.PendingComplianceReview,
            ApplicationStatus.AwaitingBranchActivation,
            ApplicationStatus.Fulfilling,
            ApplicationStatus.Rejected,
            ApplicationStatus.Expired
        },
        [ApplicationStatus.PendingComplianceReview] = new[]
        {
            ApplicationStatus.AwaitingBranchActivation,
            ApplicationStatus.Fulfilling,
            ApplicationStatus.Rejected
        },
        [ApplicationStatus.AwaitingBranchActivation] = new[]
        {
            ApplicationStatus.Fulfilling,
            ApplicationStatus.Rejected,
            ApplicationStatus.Expired
        },
        [ApplicationStatus.Fulfilling] = new[] { ApplicationStatus.Approved },
        [ApplicationStatus.Approved] = Array.Empty<ApplicationStatus>(),
        [ApplicationStatus.Rejected] = Array.Empty<ApplicationStatus>(),
        [ApplicationStatus.Expired] = Array.Empty<ApplicationStatus>()
    };

    /// <summary>Whether an application may move from one status to another.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status it would move to.</param>
    /// <returns>True when the move is allowed.</returns>
    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to)
    {
        return _Allowed[from].Contains(to);
    }

    /// <summary>Whether an application can no longer change.</summary>
    /// <param name="status">The status to check.</param>
    /// <returns>True for approved, rejected and expired applications.</returns>
    public static bool IsFinal(ApplicationStatus status)
    {
        return _Allowed[status].Length == 0;
    }

    /// <summary>
    /// Whether an application in this status prevents the same applicant from starting another.
    /// A rejected or expired applicant must be free to apply again.
    /// </summary>
    /// <param name="status">The status of the applicant's existing application.</param>
    /// <returns>True when a new application must be refused.</returns>
    public static bool BlocksNewApplication(ApplicationStatus status)
    {
        return status is not (ApplicationStatus.Rejected or ApplicationStatus.Expired);
    }

    /// <summary>
    /// The status after the applicant has been cleared, by the system or by a compliance officer.
    /// Markets that require a branch visit cannot be fulfilled remotely.
    /// </summary>
    /// <param name="market">The applicant's market.</param>
    /// <returns>The status the cleared application moves to.</returns>
    public static ApplicationStatus NextAfterClearance(MarketProfile market)
    {
        ArgumentNullException.ThrowIfNull(market);
        return market.ActivationMode == ActivationMode.InPerson
            ? ApplicationStatus.AwaitingBranchActivation
            : ApplicationStatus.Fulfilling;
    }
}
