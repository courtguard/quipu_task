namespace Atlas.Onboarding.Domain.Verification;

/// <summary>What the identity provider concluded about the identity document.</summary>
public enum DocumentResult
{
    /// <summary>The document is authentic.</summary>
    Valid = 0,

    /// <summary>The document is not authentic.</summary>
    Invalid = 1,

    /// <summary>The provider could not tell, typically because of poor image quality.</summary>
    Inconclusive = 2
}

/// <summary>
/// The result of the identity provider's check of a document and selfie.
/// </summary>
/// <param name="DocumentResult">Whether the document is authentic.</param>
/// <param name="FaceMatch">Whether the selfie matches the face on the document.</param>
/// <param name="Confidence">The provider's confidence in its answer, from 0 to 1.</param>
public sealed record IdentityCheck(
    DocumentResult DocumentResult,
    bool FaceMatch,
    double Confidence);
