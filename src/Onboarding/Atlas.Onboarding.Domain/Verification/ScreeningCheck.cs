namespace Atlas.Onboarding.Domain.Verification;

/// <summary>What sanctions and PEP screening concluded about an applicant.</summary>
public enum ScreeningStatus
{
    /// <summary>No match was found on any list.</summary>
    Clear,

    /// <summary>
    /// The applicant may be on a sanctions or PEP list. Only a compliance officer can decide.
    /// </summary>
    PossibleMatch
}

/// <summary>
/// The result of sanctions and PEP screening.
/// </summary>
/// <param name="Status">Whether the applicant is clear or a possible match.</param>
public sealed record ScreeningCheck(ScreeningStatus Status);
