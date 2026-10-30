namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// The personal details an applicant supplied. All of it is personal data.
/// </summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DateOfBirth">Date of birth.</param>
/// <param name="Nationality">
/// Nationality code. May differ from the market the applicant lives in.
/// </param>
/// <param name="NationalId">The market's national identifier, if the applicant has one.</param>
/// <param name="PassportNumber">The passport number, for markets that accept it in lieu.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number.</param>
public sealed record ApplicantDetails(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Nationality,
    string? NationalId,
    string? PassportNumber,
    string Email,
    string Phone)
{
    /// <summary>The name as sent to sanctions screening.</summary>
    public string FullName => $"{FirstName} {LastName}";
}
