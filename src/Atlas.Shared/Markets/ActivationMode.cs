namespace Atlas.Shared.Markets;

/// <summary>
/// How an approved application becomes an active account in a market.
/// </summary>
public enum ActivationMode
{
    /// <summary>The account can be activated remotely, with no visit to a branch.</summary>
    Remote,

    /// <summary>
    /// The regulator requires the applicant to attend a branch and sign in person before the
    /// account may be activated. Remote identification is still allowed for the application.
    /// </summary>
    InPerson
}
