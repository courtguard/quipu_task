namespace Atlas.Onboarding.Api.Applications;

/// <summary>
/// The body of <c>POST /applications</c>. It is the shape agreed with the mobile team in
/// ATLAS-1, plus two optional additions: <c>nationality</c> and <c>passportNumber</c>.
/// </summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DateOfBirth">Date of birth, as yyyy-MM-dd.</param>
/// <param name="Country">The market the applicant lives in, for example "MB".</param>
/// <param name="Nationality">
/// Optional. The applicant's nationality; the country when absent.
/// </param>
/// <param name="NationalId">The market's national identifier, if the applicant has one.</param>
/// <param name="PassportNumber">Optional. Accepted for a national id in some markets.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="Documents">The identity document and the selfie.</param>
/// <param name="TermsAccepted">Whether the applicant accepted the terms.</param>
public sealed record SubmitApplicationRequest(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? Country,
    string? Nationality,
    string? NationalId,
    string? PassportNumber,
    string? Email,
    string? Phone,
    List<DocumentDto>? Documents,
    bool? TermsAccepted);

/// <summary>An image in the submission.</summary>
/// <param name="Type">PASSPORT, ID_CARD or SELFIE.</param>
/// <param name="Image">The image, base64 encoded.</param>
public sealed record DocumentDto(string? Type, string? Image);

/// <summary>What the applicant is told about their application.</summary>
/// <param name="ApplicationId">The application's public identifier.</param>
/// <param name="Status">
/// APPROVED or REJECTED when decided; otherwise where it stands (PROCESSING, UNDER_REVIEW,
/// AWAITING_BRANCH_ACTIVATION or EXPIRED).
/// </param>
/// <param name="Reason">Why it was rejected, when it was.</param>
/// <param name="StatusToken">
/// The secret needed to ask about the application later. Only given when submitting.
/// </param>
public sealed record ApplicationResponse(
    string ApplicationId,
    string Status,
    string? Reason,
    string? StatusToken);

/// <summary>One thing wrong with a submission. It never contains the offending value.</summary>
/// <param name="Field">The field at fault.</param>
/// <param name="Code">A stable code the app can act on.</param>
public sealed record FieldError(string Field, string Code);

/// <summary>The answer to a request that cannot be processed.</summary>
/// <param name="Error">A stable code for the kind of failure.</param>
/// <param name="Errors">The individual field problems, for a validation failure.</param>
public sealed record ErrorResponse(string Error, IReadOnlyList<FieldError>? Errors = null);
