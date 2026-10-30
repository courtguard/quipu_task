using System.Security.Claims;
using Atlas.Shared.Auditing;

namespace Atlas.Review.Api.Security;

/// <summary>The roles staff can hold, as the identity provider names them.</summary>
public static class StaffRoles
{
    /// <summary>Reviews possible sanctions and PEP matches.</summary>
    public const string COMPLIANCE_OFFICER = "compliance_officer";

    /// <summary>Confirms that an applicant attended a branch and signed in person.</summary>
    public const string BRANCH_STAFF = "branch_staff";

    /// <summary>Handles applicants' data protection requests, such as erasure.</summary>
    public const string DATA_PROTECTION_OFFICER = "data_protection_officer";
}

/// <summary>The signed-in member of staff, taken from their token.</summary>
/// <param name="Subject">Their identifier at the identity provider.</param>
/// <param name="Markets">The markets they work for.</param>
public sealed record StaffMember(string Subject, IReadOnlySet<string> Markets)
{
    /// <summary>How they are named in the access log.</summary>
    public string Actor => AccessActors.Staff(Subject);

    /// <summary>Reads the staff member from a validated token.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>The staff member.</returns>
    public static StaffMember From(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("The token has no subject.");
        var markets = user.FindAll("markets").Select(c => c.Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new StaffMember(subject, markets);
    }

    /// <summary>
    /// Whether they may work on applications from a market. Compliance requires the review
    /// to be done by an officer in the applicant's market.
    /// </summary>
    /// <param name="market">The market code.</param>
    /// <returns>True when the market is one of theirs.</returns>
    public bool Covers(string market) => Markets.Contains(market);
}
