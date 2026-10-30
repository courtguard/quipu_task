using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Infrastructure.Providers;

/// <summary>What the identity provider concluded, with its reference.</summary>
/// <param name="Check">The verdict.</param>
/// <param name="IdentificationId">The provider's reference for the check.</param>
public sealed record IdentityVerification(IdentityCheck Check, string IdentificationId);

/// <summary>What the screening provider concluded, with its reference.</summary>
/// <param name="Check">The verdict.</param>
/// <param name="CaseId">The provider's case reference.</param>
public sealed record ScreeningOutcome(ScreeningCheck Check, string CaseId);

/// <summary>Identity verification: document authenticity and face match.</summary>
public interface IIdentityVerifier
{
    /// <summary>Checks a document and a selfie.</summary>
    /// <param name="market">The market, which decides the endpoint.</param>
    /// <param name="kind">The kind of document.</param>
    /// <param name="document">The document image.</param>
    /// <param name="selfie">The selfie image.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The verdict, or that none could be obtained.</returns>
    Task<ProviderResult<IdentityVerification>> VerifyAsync(MarketProfile market,
        DocumentKind kind, byte[] document, byte[] selfie, CancellationToken cancellationToken);
}

/// <summary>Sanctions and PEP screening.</summary>
public interface IScreeningService
{
    /// <summary>Screens an applicant.</summary>
    /// <param name="market">The market, which decides the endpoint.</param>
    /// <param name="applicant">The applicant.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The verdict, or that none could be obtained.</returns>
    Task<ProviderResult<ScreeningOutcome>> ScreenAsync(MarketProfile market,
        ApplicantDetails applicant, CancellationToken cancellationToken);
}

/// <summary>
/// Core banking. Both calls are idempotent on the key: repeating a call with the same key
/// returns the original result and never creates a second account or card order.
/// </summary>
public interface ICoreBanking
{
    /// <summary>Opens an account.</summary>
    /// <param name="market">The market, which decides the endpoint.</param>
    /// <param name="idempotencyKey">Makes a repeated call recognisable.</param>
    /// <param name="holderName">The account holder's name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The account reference, or that none could be obtained.</returns>
    Task<ProviderResult<string>> OpenAccountAsync(MarketProfile market, string idempotencyKey,
        string holderName, CancellationToken cancellationToken);

    /// <summary>Orders a card for an account.</summary>
    /// <param name="market">The market, which decides the endpoint.</param>
    /// <param name="idempotencyKey">Makes a repeated call recognisable.</param>
    /// <param name="accountId">The account the card is for.</param>
    /// <param name="holderName">The name to put on the card.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The card order reference, or that none could be obtained.</returns>
    Task<ProviderResult<string>> OrderCardAsync(MarketProfile market, string idempotencyKey,
        string accountId, string holderName, CancellationToken cancellationToken);
}
