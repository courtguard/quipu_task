using Atlas.Onboarding.Domain.Verification;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// An applicant's request to open an account, from submission to an open account or a final
/// refusal. It owns the rules about how it may change: nothing outside it can put an application
/// into a status the rules do not allow.
/// </summary>
public sealed class Application
{
    /// <summary>
    /// Years an onboarding record, rejected applications included, must be kept from the date of
    /// application (compliance position GC-2026-0814, point 4).
    /// </summary>
    public const int RETENTION_YEARS = 10;

    private readonly List<ApplicationDocument> _Documents = new();

    private Application()
    {
    }

    /// <summary>Unique identifier. It is never derived from the applicant's identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The applicant's market code, for example "MB".</summary>
    public string Market { get; private set; } = string.Empty;

    /// <summary>Where the application is in its life.</summary>
    public ApplicationStatus Status { get; private set; }

    /// <summary>Why the application was rejected, if it was. Safe to show the applicant.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Given name.</summary>
    public string FirstName { get; private set; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>Date of birth.</summary>
    public DateOnly DateOfBirth { get; private set; }

    /// <summary>Nationality code, as sent to sanctions screening.</summary>
    public string Nationality { get; private set; } = string.Empty;

    /// <summary>The market's national identifier, when the applicant has one.</summary>
    public string? NationalId { get; private set; }

    /// <summary>The passport number, when the market accepts it in lieu of a national id.</summary>
    public string? PassportNumber { get; private set; }

    /// <summary>Email address.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Phone number.</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>
    /// A keyed hash of the applicant's market and identifier, used only to refuse a second active
    /// application from the same person. It is a lookup value, never a key for the record.
    /// </summary>
    public byte[] ApplicantHash { get; private set; } = Array.Empty<byte>();

    /// <summary>The client's idempotency key, or one derived from the request.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the submitted request, to tell a retry from a new request.</summary>
    public byte[] RequestFingerprint { get; private set; } = Array.Empty<byte>();

    /// <summary>When the applicant accepted the terms, in UTC.</summary>
    public DateTime TermsAcceptedAtUtc { get; private set; }

    /// <summary>The identity provider's verdict on the document, once received.</summary>
    public DocumentResult? DocumentResult { get; private set; }

    /// <summary>Whether the selfie matched the document, once received.</summary>
    public bool? FaceMatch { get; private set; }

    /// <summary>The identity provider's confidence, once received.</summary>
    public double? Confidence { get; private set; }

    /// <summary>The identity provider's reference for its check.</summary>
    public string? IdentificationId { get; private set; }

    /// <summary>The sanctions screening verdict, once received.</summary>
    public ScreeningStatus? ScreeningStatus { get; private set; }

    /// <summary>The screening provider's case reference.</summary>
    public string? ScreeningCaseId { get; private set; }

    /// <summary>The core banking reference of the opened account.</summary>
    public string? AccountId { get; private set; }

    /// <summary>The core banking reference of the card order.</summary>
    public string? CardOrderId { get; private set; }

    /// <summary>When the application was submitted, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When the application last changed, in UTC.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>The earliest time the record may be deleted, in UTC.</summary>
    public DateTime RetentionUntilUtc { get; private set; }

    /// <summary>Concurrency token, so that two writers cannot silently overwrite.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>The images submitted with the application.</summary>
    public IReadOnlyCollection<ApplicationDocument> Documents => _Documents;

    /// <summary>The applicant's details as submitted.</summary>
    public ApplicantDetails Applicant => new(FirstName, LastName, DateOfBirth, Nationality,
        NationalId, PassportNumber, Email, Phone);

    /// <summary>The identity result, or null while it has not arrived.</summary>
    public IdentityCheck? Identity => DocumentResult is { } document && FaceMatch is { } face
        && Confidence is { } confidence
            ? new IdentityCheck(document, face, confidence)
            : null;

    /// <summary>The screening result, or null while it has not arrived.</summary>
    public ScreeningCheck? Screening => ScreeningStatus is { } status
        ? new ScreeningCheck(status)
        : null;

    /// <summary>
    /// Starts a new application, in the <see cref="ApplicationStatus.Processing" /> status.
    /// </summary>
    /// <param name="id">The new application's identifier.</param>
    /// <param name="market">The applicant's market code.</param>
    /// <param name="applicant">The applicant's details.</param>
    /// <param name="applicantHash">Keyed hash of the applicant's market and identifier.</param>
    /// <param name="idempotencyKey">The key that makes a resubmission recognisable.</param>
    /// <param name="requestFingerprint">SHA-256 of the submitted request.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <returns>The new application.</returns>
    public static Application Submit(Guid id, string market, ApplicantDetails applicant,
        byte[] applicantHash, string idempotencyKey, byte[] requestFingerprint, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(applicant);
        return new Application
        {
            Id = id,
            Market = market,
            Status = ApplicationStatus.Processing,
            FirstName = applicant.FirstName,
            LastName = applicant.LastName,
            DateOfBirth = applicant.DateOfBirth,
            Nationality = applicant.Nationality,
            NationalId = applicant.NationalId,
            PassportNumber = applicant.PassportNumber,
            Email = applicant.Email,
            Phone = applicant.Phone,
            ApplicantHash = applicantHash,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            TermsAcceptedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RetentionUntilUtc = now.AddYears(RETENTION_YEARS)
        };
    }

    /// <summary>Attaches a stored image to the application.</summary>
    /// <param name="document">The record of the stored image.</param>
    public void AddDocument(ApplicationDocument document)
    {
        _Documents.Add(document);
    }

    /// <summary>Whether a stuck application has outlived the time allowed to complete.</summary>
    /// <param name="now">The current time, in UTC.</param>
    /// <param name="maxAge">How long an application may stay in processing.</param>
    /// <returns>True when it is processing and older than <paramref name="maxAge" />.</returns>
    public bool HasOutlived(DateTime now, TimeSpan maxAge)
    {
        return Status == ApplicationStatus.Processing && now - CreatedAtUtc >= maxAge;
    }

    /// <summary>Records the identity provider's answer.</summary>
    /// <param name="check">The answer.</param>
    /// <param name="identificationId">The provider's reference.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <exception cref="InvalidOperationException">
    /// The application is not processing, or already has an identity result.
    /// </exception>
    public void RecordIdentity(IdentityCheck check, string identificationId, DateTime now)
    {
        RequireStatus(ApplicationStatus.Processing);
        if (Identity is not null)
        {
            throw new InvalidOperationException("The identity result is already recorded.");
        }

        DocumentResult = check.DocumentResult;
        FaceMatch = check.FaceMatch;
        Confidence = check.Confidence;
        IdentificationId = identificationId;
        UpdatedAtUtc = now;
    }

    /// <summary>Records the screening provider's answer.</summary>
    /// <param name="check">The answer.</param>
    /// <param name="caseId">The provider's case reference.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <exception cref="InvalidOperationException">
    /// The application is not processing, or already has a screening result.
    /// </exception>
    public void RecordScreening(ScreeningCheck check, string caseId, DateTime now)
    {
        RequireStatus(ApplicationStatus.Processing);
        if (Screening is not null)
        {
            throw new InvalidOperationException("The screening result is already recorded.");
        }

        ScreeningStatus = check.Status;
        ScreeningCaseId = caseId;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Acts on what the checks so far allow: rejects, refers to review, clears, or does nothing
    /// when a result is still missing.
    /// </summary>
    /// <param name="market">The applicant's market.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <returns>True when the application changed status.</returns>
    /// <exception cref="InvalidOperationException">The application is not processing.</exception>
    public bool ApplyVerification(MarketProfile market, DateTime now)
    {
        RequireStatus(ApplicationStatus.Processing);
        var decision = VerificationDecider.Decide(market, Identity, Screening);
        switch (decision.Kind)
        {
            case DecisionKind.Reject:
                RejectionReason = decision.Reason;
                MoveTo(ApplicationStatus.Rejected, now);
                return true;
            case DecisionKind.Review:
                MoveTo(ApplicationStatus.PendingComplianceReview, now);
                return true;
            case DecisionKind.Clear:
                MoveTo(ApplicationStatusRules.NextAfterClearance(market), now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Gives up on an application that could not be completed in time, for example because a
    /// provider stayed unavailable. This is not a judgement on the applicant.
    /// </summary>
    /// <param name="now">The current time, in UTC.</param>
    public void Expire(DateTime now)
    {
        MoveTo(ApplicationStatus.Expired, now);
    }

    /// <summary>Records that the applicant attended the branch and signed.</summary>
    /// <param name="now">The current time, in UTC.</param>
    public void ActivateAtBranch(DateTime now)
    {
        RequireStatus(ApplicationStatus.AwaitingBranchActivation);
        MoveTo(ApplicationStatus.Fulfilling, now);
    }

    /// <summary>Records the opened account.</summary>
    /// <param name="accountId">Core banking's reference for the account.</param>
    /// <param name="now">The current time, in UTC.</param>
    public void RecordAccount(string accountId, DateTime now)
    {
        RequireStatus(ApplicationStatus.Fulfilling);
        if (AccountId is not null)
        {
            throw new InvalidOperationException("The account is already recorded.");
        }

        AccountId = accountId;
        UpdatedAtUtc = now;
    }

    /// <summary>Records the card order, which can only follow the account.</summary>
    /// <param name="cardOrderId">Core banking's reference for the card order.</param>
    /// <param name="now">The current time, in UTC.</param>
    public void RecordCardOrder(string cardOrderId, DateTime now)
    {
        RequireStatus(ApplicationStatus.Fulfilling);
        if (AccountId is null)
        {
            throw new InvalidOperationException("A card cannot be ordered before the account.");
        }

        if (CardOrderId is not null)
        {
            throw new InvalidOperationException("The card order is already recorded.");
        }

        CardOrderId = cardOrderId;
        UpdatedAtUtc = now;
    }

    /// <summary>Approves the application, which needs both an account and a card order.</summary>
    /// <param name="now">The current time, in UTC.</param>
    public void Approve(DateTime now)
    {
        RequireStatus(ApplicationStatus.Fulfilling);
        if (AccountId is null || CardOrderId is null)
        {
            throw new InvalidOperationException(
                "An application is approved only with an open account and an ordered card.");
        }

        MoveTo(ApplicationStatus.Approved, now);
    }

    private void RequireStatus(ApplicationStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"The application is {Status}, but this needs it to be {expected}.");
        }
    }

    private void MoveTo(ApplicationStatus next, DateTime now)
    {
        if (!ApplicationStatusRules.CanTransition(Status, next))
        {
            throw new InvalidOperationException(
                $"An application cannot go from {Status} to {next}.");
        }

        Status = next;
        UpdatedAtUtc = now;
    }
}
