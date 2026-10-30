using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Domain.Verification;

/// <summary>
/// Decides what happens to an application from the results of identity verification and
/// sanctions screening. The rules are:
/// <list type="bullet">
/// <item>A failed identity check rejects the application at once; screening is not awaited.</item>
/// <item>A possible sanctions or PEP match is never decided by the system; it is reviewed.</item>
/// <item>A missing result means wait. A provider outage never produces a rejection.</item>
/// </list>
/// </summary>
public static class VerificationDecider
{
    /// <summary>Weighs the results received so far.</summary>
    /// <param name="market">The applicant's market, which sets the confidence threshold.</param>
    /// <param name="identity">The identity result, or null if not available yet.</param>
    /// <param name="screening">The screening result, or null if not available yet.</param>
    /// <returns>What should happen to the application.</returns>
    public static VerificationDecision Decide(MarketProfile market, IdentityCheck? identity,
        ScreeningCheck? screening)
    {
        ArgumentNullException.ThrowIfNull(market);

        if (identity is null)
        {
            // Even a possible match waits: the officer needs the identity result to decide.
            return new VerificationDecision(DecisionKind.Wait);
        }

        var failure = FindIdentityFailure(market, identity);
        if (failure is not null)
        {
            return new VerificationDecision(DecisionKind.Reject, failure);
        }

        if (screening is null)
        {
            return new VerificationDecision(DecisionKind.Wait);
        }

        return screening.Status == ScreeningStatus.PossibleMatch
            ? new VerificationDecision(DecisionKind.Review)
            : new VerificationDecision(DecisionKind.Clear);
    }

    private static string? FindIdentityFailure(MarketProfile market, IdentityCheck identity)
    {
        // Definite negatives first, then the cases where the applicant can simply try again.
        if (identity.DocumentResult == DocumentResult.Invalid)
        {
            return RejectionReasons.DOCUMENT_INVALID;
        }

        if (!identity.FaceMatch)
        {
            return RejectionReasons.FACE_MISMATCH;
        }

        // Written as a negated >= so that a NaN confidence counts as too low.
        if (identity.DocumentResult == DocumentResult.Inconclusive
            || !(identity.Confidence >= market.IdNowMinConfidence))
        {
            return RejectionReasons.IDENTITY_NOT_VERIFIED_RETRY;
        }

        return null;
    }
}
