namespace Atlas.Shared.Applications;

/// <summary>
/// What a member of staff decided about an application. The Review API records it; the
/// onboarding service applies it. The numbers are stored, so they must never change.
/// </summary>
public enum StaffDecisionKind
{
    /// <summary>A compliance officer cleared a possible sanctions or PEP match.</summary>
    ApproveReview = 0,

    /// <summary>A compliance officer declined the application after review.</summary>
    RejectReview = 1,

    /// <summary>Branch staff confirmed the applicant attended and signed in person.</summary>
    BranchActivated = 2
}
