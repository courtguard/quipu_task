namespace Atlas.Shared.Auditing;

/// <summary>
/// The kinds of access to customer data that are recorded (compliance position GC-2026-0814,
/// point 2). Every entry names who made the access; none contains personal data itself.
/// </summary>
public static class AccessActions
{
    /// <summary>The applicant submitted the application.</summary>
    public const string SUBMITTED = "SUBMITTED";

    /// <summary>The applicant read the status, with the status token.</summary>
    public const string STATUS_READ = "STATUS_READ";

    /// <summary>The documents and selfie were sent to the identity provider.</summary>
    public const string SENT_TO_IDENTITY_PROVIDER = "SENT_TO_IDENTITY_PROVIDER";

    /// <summary>The applicant's details were sent to the screening provider.</summary>
    public const string SENT_TO_SCREENING_PROVIDER = "SENT_TO_SCREENING_PROVIDER";

    /// <summary>The applicant's name was sent to core banking.</summary>
    public const string SENT_TO_CORE_BANKING = "SENT_TO_CORE_BANKING";

    /// <summary>A member of staff viewed the applicant's details.</summary>
    public const string CASE_VIEWED = "CASE_VIEWED";

    /// <summary>A member of staff recorded a decision.</summary>
    public const string DECISION_RECORDED = "DECISION_RECORDED";

    /// <summary>The onboarding service applied a staff decision to the application.</summary>
    public const string DECISION_APPLIED = "DECISION_APPLIED";
}

/// <summary>How the people and systems that access customer data are named in the log.</summary>
public static class AccessActors
{
    /// <summary>The onboarding service itself.</summary>
    public const string ONBOARDING_SERVICE = "service:atlas-onboarding";

    /// <summary>The applicant, proven by their status token or by submitting.</summary>
    public const string APPLICANT = "applicant";

    /// <summary>A named member of staff.</summary>
    /// <param name="subject">The staff member's identifier from the identity provider.</param>
    /// <returns>The actor name.</returns>
    public static string Staff(string subject) => "staff:" + subject;
}
