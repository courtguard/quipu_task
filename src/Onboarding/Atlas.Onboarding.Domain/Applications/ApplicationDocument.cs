namespace Atlas.Onboarding.Domain.Applications;

/// <summary>The kinds of image an applicant submits.</summary>
public enum DocumentKind
{
    /// <summary>A passport.</summary>
    Passport,

    /// <summary>A national identity card.</summary>
    IdCard,

    /// <summary>A photograph of the applicant's face, used for liveness and face match.</summary>
    Selfie
}

/// <summary>
/// An image submitted with an application. The image itself lives in blob storage; this is the
/// record of where, and of what was stored.
/// </summary>
public sealed class ApplicationDocument
{
    private ApplicationDocument()
    {
    }

    /// <summary>Creates the record of a stored image.</summary>
    /// <param name="id">Unique identifier of the document.</param>
    /// <param name="applicationId">The application it belongs to.</param>
    /// <param name="kind">What the image shows.</param>
    /// <param name="blobName">Name of the stored blob in the market's container.</param>
    /// <param name="sha256">SHA-256 of the image bytes.</param>
    /// <param name="sizeBytes">Size of the image in bytes.</param>
    /// <param name="now">The current time, in UTC.</param>
    public ApplicationDocument(Guid id, Guid applicationId, DocumentKind kind, string blobName,
        byte[] sha256, long sizeBytes, DateTime now)
    {
        Id = id;
        ApplicationId = applicationId;
        Kind = kind;
        BlobName = blobName;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        CreatedAtUtc = now;
    }

    /// <summary>Unique identifier of the document.</summary>
    public Guid Id { get; private set; }

    /// <summary>The application the document belongs to.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>What the image shows.</summary>
    public DocumentKind Kind { get; private set; }

    /// <summary>Name of the stored blob in the market's container.</summary>
    public string BlobName { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the image bytes, to detect corruption or substitution.</summary>
    public byte[] Sha256 { get; private set; } = Array.Empty<byte>();

    /// <summary>Size of the image in bytes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>When the image was stored, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }
}
