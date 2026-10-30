namespace Atlas.Onboarding.Domain.Verification;

/// <summary>What should happen to an application given the checks done so far.</summary>
public enum DecisionKind
{
    /// <summary>A required check has no result yet, so nothing can be decided.</summary>
    Wait,

    /// <summary>The applicant passed every check and is cleared.</summary>
    Clear,

    /// <summary>The applicant failed identity verification and is declined.</summary>
    Reject,

    /// <summary>A compliance officer must decide; the system must not.</summary>
    Review
}

/// <summary>Reason codes for declined applications, safe to show to the applicant.</summary>
public static class RejectionReasons
{
    /// <summary>The document is not authentic.</summary>
    public const string DOCUMENT_INVALID = "DOCUMENT_INVALID";

    /// <summary>The selfie does not match the document.</summary>
    public const string FACE_MISMATCH = "FACE_MISMATCH";

    /// <summary>
    /// The identity could not be established with enough confidence, typically because of image
    /// quality. The applicant can try again with better photographs.
    /// </summary>
    public const string IDENTITY_NOT_VERIFIED_RETRY = "IDENTITY_NOT_VERIFIED_RETRY";
}

/// <summary>
/// The outcome of weighing the verification and screening results.
/// </summary>
/// <param name="Kind">What should happen next.</param>
/// <param name="Reason">
/// For <see cref="DecisionKind.Reject" />, one of <see cref="RejectionReasons" />; otherwise null.
/// </param>
public sealed record VerificationDecision(DecisionKind Kind, string? Reason = null);
