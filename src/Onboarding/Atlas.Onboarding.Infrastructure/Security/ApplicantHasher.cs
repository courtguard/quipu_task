using System.Security.Cryptography;
using System.Text;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Security;

/// <summary>Secret keys held by the service. In production they come from the vault.</summary>
public sealed class SecretsOptions
{
    /// <summary>Base64 key for hashing applicant identifiers. At least 32 bytes.</summary>
    public string ApplicantHashKey { get; set; } = string.Empty;

    /// <summary>Base64 key for deriving status tokens. At least 32 bytes.</summary>
    public string StatusTokenKey { get; set; } = string.Empty;

    /// <summary>Decodes a base64 key and checks it is strong enough.</summary>
    /// <param name="base64">The encoded key.</param>
    /// <param name="name">The setting's name, for the error message.</param>
    /// <returns>The key bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// The key is missing, malformed or too short.
    /// </exception>
    public static byte[] DecodeKey(string base64, string name)
    {
        try
        {
            var key = Convert.FromBase64String(base64);
            if (key.Length >= 32)
            {
                return key;
            }
        }
        catch (FormatException)
        {
            // Reported below, with the same message as a short key.
        }

        throw new InvalidOperationException($"{name} must be a base64 value of at least 32 bytes.");
    }
}

/// <summary>
/// Turns an applicant's identifier into a value that can find a duplicate application without
/// keeping the identifier in the clear in a second place. It is a keyed hash, so the value cannot
/// be reversed or matched against a list of identifiers by anyone without the key. It is only a
/// lookup: it is never a key for customer records.
/// </summary>
public sealed class ApplicantHasher
{
    private readonly byte[] _Key;

    /// <summary>Creates the hasher.</summary>
    /// <param name="options">The service's secrets.</param>
    public ApplicantHasher(IOptions<SecretsOptions> options)
    {
        _Key = SecretsOptions.DecodeKey(options.Value.ApplicantHashKey,
            nameof(SecretsOptions.ApplicantHashKey));
    }

    /// <summary>Hashes the identifier of an applicant within their market.</summary>
    /// <param name="market">The market code.</param>
    /// <param name="applicant">The applicant.</param>
    /// <returns>A 32-byte hash.</returns>
    public byte[] Hash(string market, ApplicantDetails applicant)
    {
        var identifier = string.IsNullOrWhiteSpace(applicant.NationalId)
            ? "passport:" + applicant.PassportNumber
            : "id:" + applicant.NationalId;
        var text = $"{market.ToUpperInvariant()}|{identifier.Trim().ToUpperInvariant()}";
        return HMACSHA256.HashData(_Key, Encoding.UTF8.GetBytes(text));
    }
}
