using System.Text.RegularExpressions;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Identity;
using Atlas.Onboarding.Infrastructure.Submission;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>The result of checking a submission.</summary>
/// <param name="Errors">What is wrong; empty when the submission is acceptable.</param>
/// <param name="Market">The applicant's market, when it could be determined.</param>
/// <param name="Applicant">The applicant's details, when valid.</param>
/// <param name="Documents">The decoded images, when valid.</param>
public sealed record ValidationOutcome(
    IReadOnlyList<FieldError> Errors,
    MarketProfile? Market,
    ApplicantDetails? Applicant,
    IReadOnlyList<SubmittedDocument>? Documents)
{
    /// <summary>Whether the submission can be recorded.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks a submission before anything is stored. It reports every problem at once and never
/// repeats a submitted value back, because they are personal data.
/// </summary>
public sealed partial class SubmissionValidator
{
    private const int MAX_NAME_LENGTH = 100;
    private const int MAX_IMAGE_BYTES = 15 * 1024 * 1024;

    private readonly MarketRegistry _Markets;
    private readonly IdentifierValidator _Identifiers;
    private readonly TimeProvider _Time;

    /// <summary>Creates the validator.</summary>
    public SubmissionValidator(MarketRegistry markets, IdentifierValidator identifiers,
        TimeProvider time)
    {
        _Markets = markets;
        _Identifiers = identifiers;
        _Time = time;
    }

    /// <summary>Checks a submission.</summary>
    /// <param name="request">The deserialised body.</param>
    /// <returns>The problems found, or the checked, normalised submission.</returns>
    public ValidationOutcome Validate(SubmitApplicationRequest request)
    {
        var errors = new List<FieldError>();
        var market = FindMarket(request.Country, errors);

        var firstName = RequireName(request.FirstName, "firstName", errors);
        var lastName = RequireName(request.LastName, "lastName", errors);
        var dateOfBirth = CheckDateOfBirth(request.DateOfBirth, errors);
        var nationality = CheckNationality(request.Nationality, request.Country, errors);
        var email = CheckEmail(request.Email, errors);
        var phone = CheckPhone(request.Phone, errors);

        if (market is not null)
        {
            foreach (var code in _Identifiers
                         .Validate(market, request.NationalId, request.PassportNumber).Errors)
            {
                var field = code == IdentifierValidator.PASSPORT_NUMBER_INVALID_FORMAT
                    ? "passportNumber"
                    : "nationalId";
                errors.Add(new FieldError(field, code));
            }
        }

        var documents = CheckDocuments(request.Documents, errors);
        if (request.TermsAccepted != true)
        {
            errors.Add(new FieldError("termsAccepted", "MUST_BE_TRUE"));
        }

        if (errors.Count > 0 || market is null)
        {
            return new ValidationOutcome(errors, market, null, null);
        }

        var applicant = new ApplicantDetails(firstName!, lastName!, dateOfBirth!.Value,
            nationality!, Trimmed(request.NationalId), Trimmed(request.PassportNumber), email!,
            phone!);
        return new ValidationOutcome(errors, market, applicant, documents);
    }

    private MarketProfile? FindMarket(string? country, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            errors.Add(new FieldError("country", "REQUIRED"));
            return null;
        }

        var market = _Markets.Find(country);
        if (market is null || !market.Enabled)
        {
            errors.Add(new FieldError("country", "NOT_SUPPORTED"));
            return null;
        }

        return market;
    }

    private static string? RequireName(string? value, string field, List<FieldError> errors)
    {
        var name = Trimmed(value);
        if (name is null)
        {
            errors.Add(new FieldError(field, "REQUIRED"));
        }
        else if (name.Length > MAX_NAME_LENGTH)
        {
            errors.Add(new FieldError(field, "TOO_LONG"));
            return null;
        }

        return name;
    }

    private DateOnly? CheckDateOfBirth(DateOnly? value, List<FieldError> errors)
    {
        if (value is null)
        {
            errors.Add(new FieldError("dateOfBirth", "REQUIRED"));
            return null;
        }

        var today = DateOnly.FromDateTime(_Time.GetUtcNow().UtcDateTime);
        if (value.Value >= today || value.Value.Year < 1900)
        {
            errors.Add(new FieldError("dateOfBirth", "INVALID"));
            return null;
        }

        return value;
    }

    private static string? CheckNationality(string? nationality, string? country,
        List<FieldError> errors)
    {
        // Optional. The ticket's API has only "country", which it also sends as the nationality,
        // so that is the fallback.
        var code = Trimmed(nationality) ?? Trimmed(country);
        if (code is not null && !NationalityPattern().IsMatch(code))
        {
            errors.Add(new FieldError("nationality", "INVALID"));
            return null;
        }

        return code?.ToUpperInvariant();
    }

    private static string? CheckEmail(string? value, List<FieldError> errors)
    {
        var email = Trimmed(value);
        if (email is null || email.Length > 254 || !EmailPattern().IsMatch(email))
        {
            errors.Add(new FieldError("email", email is null ? "REQUIRED" : "INVALID"));
            return null;
        }

        return email;
    }

    private static string? CheckPhone(string? value, List<FieldError> errors)
    {
        var phone = Trimmed(value);
        if (phone is null || !PhonePattern().IsMatch(phone))
        {
            errors.Add(new FieldError("phone", phone is null ? "REQUIRED" : "INVALID"));
            return null;
        }

        return phone;
    }

    private static List<SubmittedDocument>? CheckDocuments(List<DocumentDto>? documents,
        List<FieldError> errors)
    {
        var kinds = (documents ?? new List<DocumentDto>())
            .Select(d => KindOf(d.Type)).ToList();
        var identityDocuments = kinds.Count(k => k is DocumentKind.Passport or DocumentKind.IdCard);
        var selfies = kinds.Count(k => k == DocumentKind.Selfie);

        // Exactly one identity document and one selfie, and nothing unrecognised.
        if (kinds.Count != 2 || kinds.Contains(null) || identityDocuments != 1 || selfies != 1)
        {
            errors.Add(new FieldError("documents", "EXPECTED_ONE_DOCUMENT_AND_ONE_SELFIE"));
            return null;
        }

        var decoded = new List<SubmittedDocument>();
        for (var i = 0; i < documents!.Count; i++)
        {
            var content = DecodeImage(documents[i].Image);
            if (content is null)
            {
                errors.Add(new FieldError($"documents[{i}].image", "INVALID_IMAGE"));
            }
            else if (content.Length > MAX_IMAGE_BYTES)
            {
                errors.Add(new FieldError($"documents[{i}].image", "TOO_LARGE"));
            }
            else
            {
                decoded.Add(new SubmittedDocument(kinds[i]!.Value, content));
            }
        }

        return decoded.Count == documents.Count ? decoded : null;
    }

    private static DocumentKind? KindOf(string? type)
    {
        return type?.Trim().ToUpperInvariant() switch
        {
            "PASSPORT" => DocumentKind.Passport,
            "ID_CARD" => DocumentKind.IdCard,
            "SELFIE" => DocumentKind.Selfie,
            _ => null
        };
    }

    private static byte[]? DecodeImage(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Trimmed(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [GeneratedRegex("^[A-Za-z]{2,3}$", RegexOptions.None, 100)]
    private static partial Regex NationalityPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, 100)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,32}$", RegexOptions.None, 100)]
    private static partial Regex PhonePattern();
}
