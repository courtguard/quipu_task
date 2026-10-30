using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Security;

/// <summary>
/// The secret an applicant needs to ask about their application. It is derived from the
/// application's identifier and a server-side key, so nothing has to be stored, and a repeated
/// submission of the same request can be given the same token again.
/// </summary>
public sealed class StatusTokens
{
    private readonly byte[] _Key;

    /// <summary>Creates the token service.</summary>
    /// <param name="options">The service's secrets.</param>
    public StatusTokens(IOptions<SecretsOptions> options)
    {
        _Key = SecretsOptions.DecodeKey(options.Value.StatusTokenKey,
            nameof(SecretsOptions.StatusTokenKey));
    }

    /// <summary>Derives the token for an application.</summary>
    /// <param name="applicationId">The application's unique identifier.</param>
    /// <returns>The token, safe to put in a header.</returns>
    public string Create(Guid applicationId)
    {
        var hash = HMACSHA256.HashData(_Key, Encoding.UTF8.GetBytes("status|" + applicationId));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Checks a presented token, without leaking how much of it was right.</summary>
    /// <param name="applicationId">The application's unique identifier.</param>
    /// <param name="presented">The token the caller sent. May be null.</param>
    /// <returns>True when the token is the right one for this application.</returns>
    public bool IsValid(Guid applicationId, string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Create(applicationId));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(presented));
    }
}
