using System.Security.Claims;
using Atlas.Review.Api.Data;
using Atlas.Review.Api.Security;
using Atlas.Shared.Applications;
using Atlas.Shared.Markets;

namespace Atlas.Review.Api.Endpoints;

/// <summary>A waiting case in a list; it carries no personal data.</summary>
/// <param name="ApplicationId">The application's public identifier.</param>
/// <param name="Market">The applicant's market.</param>
/// <param name="SubmittedAtUtc">When the application was submitted.</param>
/// <param name="ScreeningCaseId">The screening provider's case reference.</param>
public sealed record CaseSummary(string ApplicationId, string Market, DateTime SubmittedAtUtc,
    string? ScreeningCaseId);

/// <summary>A waiting case with the applicant's details. Viewing it is logged.</summary>
public sealed record CaseDetails(
    string ApplicationId,
    string Market,
    string Stage,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Nationality,
    string? NationalId,
    string? PassportNumber,
    string? DocumentResult,
    bool? FaceMatch,
    double? Confidence,
    string? ScreeningCaseId,
    DateTime SubmittedAtUtc);

/// <summary>A compliance officer's decision.</summary>
/// <param name="Decision">APPROVE or REJECT.</param>
/// <param name="Note">An optional note, kept with the decision.</param>
public sealed record ReviewDecisionRequest(string? Decision, string? Note);

/// <summary>A data protection officer's record of an applicant's erasure request.</summary>
/// <param name="Reference">The reference of the applicant's request, for example a ticket.</param>
public sealed record ErasureRequest(string? Reference);

/// <summary>Branch staff's confirmation that the applicant attended and signed.</summary>
/// <param name="Note">An optional note, kept with the confirmation.</param>
public sealed record BranchActivationRequest(string? Note);

/// <summary>
/// The staff-facing API. Compliance officers decide referred applications; branch staff confirm
/// in-person visits. Staff only ever see applications from their own markets, and a case from
/// another market looks exactly like one that does not exist.
/// </summary>
public static class StaffEndpoints
{
    /// <summary>The policy for compliance officers.</summary>
    public const string OFFICERS = "ComplianceOfficers";

    /// <summary>The policy for branch staff.</summary>
    public const string BRANCH = "BranchStaff";

    /// <summary>The policy for data protection officers.</summary>
    public const string DATA_PROTECTION = "DataProtectionOfficers";

    private const int MAX_NOTE_LENGTH = 1000;
    private const int MAX_REFERENCE_LENGTH = 100;

    /// <summary>Maps the review and branch activation endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapStaff(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapGroup("/reviews").RequireAuthorization(OFFICERS);
        reviews.MapGet("", (string? market, ClaimsPrincipal user, MarketRegistry markets,
                StaffCases cases, CancellationToken ct) =>
            ListAsync(StaffCases.REVIEW, market, user, markets, cases, ct));
        reviews.MapGet("/{id}", (string id, ClaimsPrincipal user, MarketRegistry markets,
                StaffCases cases, CancellationToken ct) =>
            ViewAsync(StaffCases.REVIEW, id, user, markets, cases, ct));
        reviews.MapPost("/{id}/decision", (string id, ReviewDecisionRequest request,
                ClaimsPrincipal user, MarketRegistry markets, StaffCases cases,
                CancellationToken ct) =>
        {
            var kind = request.Decision?.Trim().ToUpperInvariant() switch
            {
                "APPROVE" => StaffDecisionKind.ApproveReview,
                "REJECT" => StaffDecisionKind.RejectReview,
                _ => (StaffDecisionKind?)null
            };
            return kind is null
                ? Task.FromResult(Error(400, "DECISION_MUST_BE_APPROVE_OR_REJECT"))
                : RecordAsync(StaffCases.REVIEW, id, kind.Value, request.Note, user, markets,
                    cases, ct);
        });

        var branch = app.MapGroup("/branch-activations").RequireAuthorization(BRANCH);
        branch.MapGet("", (string? market, ClaimsPrincipal user, MarketRegistry markets,
                StaffCases cases, CancellationToken ct) =>
            ListAsync(StaffCases.BRANCH, market, user, markets, cases, ct));
        branch.MapGet("/{id}", (string id, ClaimsPrincipal user, MarketRegistry markets,
                StaffCases cases, CancellationToken ct) =>
            ViewAsync(StaffCases.BRANCH, id, user, markets, cases, ct));
        branch.MapPost("/{id}", (string id, BranchActivationRequest? request,
                ClaimsPrincipal user, MarketRegistry markets, StaffCases cases,
                CancellationToken ct) =>
            RecordAsync(StaffCases.BRANCH, id, StaffDecisionKind.BranchActivated,
                request?.Note, user, markets, cases, ct));

        // The applicant's identity is checked through the bank's own channel before this is
        // called; the reference ties the restriction to that request.
        app.MapPost("/erasure-requests/{id}", async (string id, ErasureRequest request,
                ClaimsPrincipal user, MarketRegistry markets, StaffCases cases,
                CancellationToken ct) =>
            {
                var reference = request.Reference?.Trim();
                if (string.IsNullOrEmpty(reference) || reference.Length > MAX_REFERENCE_LENGTH)
                {
                    return Error(400, "REFERENCE_REQUIRED");
                }

                var staff = StaffMember.From(user);
                var (market, applicationId) = Locate(id, staff, markets);
                if (market is null)
                {
                    return Error(404, "NOT_FOUND");
                }

                var outcome = await cases.RecordErasureAsync(market, applicationId, staff.Actor,
                    reference, ct);
                return outcome switch
                {
                    RecordOutcome.Recorded => Results.Accepted(value: new
                    {
                        applicationId = ApplicationIds.Format(market.Code, applicationId),
                        status = "RESTRICTION_RECORDED"
                    }),
                    RecordOutcome.AlreadyDecided => Error(409, "ALREADY_REQUESTED"),
                    _ => Error(404, "NOT_FOUND")
                };
            })
            .RequireAuthorization(DATA_PROTECTION);
    }

    private static async Task<IResult> ListAsync(string stage, string? market,
        ClaimsPrincipal user, MarketRegistry markets, StaffCases cases, CancellationToken ct)
    {
        var staff = StaffMember.From(user);
        if (market is not null && !staff.Covers(market))
        {
            return Error(403, "MARKET_NOT_ALLOWED");
        }

        var wanted = markets.All
            .Where(m => m.Enabled && staff.Covers(m.Code)
                                  && (market is null || m.Code.Equals(market,
                                      StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var summaries = new List<CaseSummary>();
        foreach (var profile in wanted)
        {
            summaries.AddRange((await cases.ListAsync(profile, stage, ct)).Select(c =>
                new CaseSummary(ApplicationIds.Format(c.Market, c.ApplicationId), c.Market,
                    c.SubmittedAtUtc, c.ScreeningCaseId)));
        }

        return Results.Ok(summaries);
    }

    private static async Task<IResult> ViewAsync(string stage, string id, ClaimsPrincipal user,
        MarketRegistry markets, StaffCases cases, CancellationToken ct)
    {
        var staff = StaffMember.From(user);
        var (market, applicationId) = Locate(id, staff, markets);
        if (market is null)
        {
            return Error(404, "NOT_FOUND");
        }

        var found = await cases.ViewAsync(market, applicationId, stage, staff.Actor, ct);
        return found is null
            ? Error(404, "NOT_FOUND")
            : Results.Ok(new CaseDetails(ApplicationIds.Format(found.Market, found.ApplicationId),
                found.Market, found.Stage, found.FirstName, found.LastName, found.DateOfBirth,
                found.Nationality, found.NationalId, found.PassportNumber, found.DocumentResult,
                found.FaceMatch, found.Confidence, found.ScreeningCaseId, found.SubmittedAtUtc));
    }

    private static async Task<IResult> RecordAsync(string stage, string id,
        StaffDecisionKind kind, string? note, ClaimsPrincipal user, MarketRegistry markets,
        StaffCases cases, CancellationToken ct)
    {
        if (note is { Length: > MAX_NOTE_LENGTH })
        {
            return Error(400, "NOTE_TOO_LONG");
        }

        var staff = StaffMember.From(user);
        var (market, applicationId) = Locate(id, staff, markets);
        if (market is null)
        {
            return Error(404, "NOT_FOUND");
        }

        var outcome = await cases.RecordAsync(market, applicationId, stage, kind, staff.Actor,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(), ct);
        return outcome switch
        {
            RecordOutcome.Recorded => Results.Accepted(value: new
            {
                applicationId = ApplicationIds.Format(market.Code, applicationId),
                decision = kind.ToString(),
                status = "RECORDED"
            }),
            RecordOutcome.AlreadyDecided => Error(409, "ALREADY_DECIDED"),
            _ => Error(404, "NOT_FOUND")
        };
    }

    // An application the staff member may not see is reported exactly like a missing one.
    private static (MarketProfile? Market, Guid Id) Locate(string id, StaffMember staff,
        MarketRegistry markets)
    {
        var parsed = ApplicationIds.Parse(id);
        if (parsed is null || !staff.Covers(parsed.Value.Market))
        {
            return (null, Guid.Empty);
        }

        var market = markets.Find(parsed.Value.Market);
        return market is { Enabled: true } ? (market, parsed.Value.Id) : (null, Guid.Empty);
    }

    private static IResult Error(int status, string code)
    {
        return Results.Json(new { error = code }, statusCode: status);
    }
}
