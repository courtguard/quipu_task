using Atlas.Shared.Applications;

namespace Atlas.Onboarding.Infrastructure.Persistence;

/// <summary>
/// A decision by a member of staff, recorded by the Review API and applied by the onboarding
/// service. Keeping the two steps apart means only the onboarding service ever changes an
/// application, through its own rules.
/// </summary>
public sealed class StaffDecision
{
    private StaffDecision()
    {
    }

    /// <summary>Unique identifier of the decision.</summary>
    public Guid Id { get; private set; }

    /// <summary>The application decided on.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>What was decided.</summary>
    public StaffDecisionKind Kind { get; private set; }

    /// <summary>Who decided, as named by the identity provider.</summary>
    public string DecidedBy { get; private set; } = string.Empty;

    /// <summary>The staff member's note, if any.</summary>
    public string? Note { get; private set; }

    /// <summary>When it was decided, in UTC.</summary>
    public DateTime DecidedAtUtc { get; private set; }

    /// <summary>When the onboarding service applied it, in UTC; null until then.</summary>
    public DateTime? AppliedAtUtc { get; private set; }

    /// <summary>
    /// What applying it did: APPLIED, or NOT_APPLICABLE when the application was no longer in
    /// the status the decision was about.
    /// </summary>
    public string? Outcome { get; private set; }

    /// <summary>Records that the decision has been dealt with.</summary>
    /// <param name="outcome">What applying it did.</param>
    /// <param name="now">The current time, in UTC.</param>
    public void MarkApplied(string outcome, DateTime now)
    {
        Outcome = outcome;
        AppliedAtUtc = now;
    }
}

/// <summary>
/// One recorded access to an applicant's data. The table is append-only: no service login may
/// change or delete a row.
/// </summary>
public sealed class AccessLogEntry
{
    private AccessLogEntry()
    {
    }

    /// <summary>Records an access.</summary>
    /// <param name="applicationId">Whose data was accessed.</param>
    /// <param name="actor">Who made the access.</param>
    /// <param name="action">What was done.</param>
    /// <param name="now">The current time, in UTC.</param>
    public AccessLogEntry(Guid applicationId, string actor, string action, DateTime now)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        Actor = actor;
        Action = action;
        OccurredAtUtc = now;
    }

    /// <summary>
    /// Unique identifier of the entry. Made by the client, not the database, so that writing an
    /// entry needs no permission to read the log back.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>Whose data was accessed.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>Who made the access: a named person, the applicant, or a named service.</summary>
    public string Actor { get; private set; } = string.Empty;

    /// <summary>What was done.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>When, in UTC.</summary>
    public DateTime OccurredAtUtc { get; private set; }
}
