using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Infrastructure.Processing;
using Atlas.Onboarding.Infrastructure.Security;
using Atlas.Onboarding.Infrastructure.Submission;
using Atlas.Shared.Applications;
using Atlas.Shared.Markets;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>The HTTP surface for applications.</summary>
public static partial class ApplicationEndpoints
{
    private const string IDEMPOTENCY_HEADER = "Idempotency-Key";
    private const string STATUS_TOKEN_HEADER = "X-Status-Token";
    private const int MAX_KEY_LENGTH = 128;
    private const int RETRY_AFTER_SECONDS = 5;

    private static readonly JsonSerializerOptions _Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>POST /applications</c> and <c>GET /applications/{id}</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapApplications(this IEndpointRouteBuilder app)
    {
        app.MapPost("/applications", SubmitAsync);
        app.MapGet("/applications/{id}", GetAsync);
    }

    private static async Task<IResult> SubmitAsync(HttpRequest http, SubmissionValidator validator,
        ApplicationSubmitter submitter, ApplicationReader reader, StatusTokens tokens,
        IOptions<ProcessingOptions> options, CancellationToken cancellationToken)
    {
        // The whole body is read once, so that it can be both fingerprinted and deserialised.
        using var buffer = new MemoryStream();
        await http.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();

        var request = Deserialize(body);
        if (request is null)
        {
            return Results.Json(new ErrorResponse("INVALID_JSON"), statusCode: 400);
        }

        var outcome = validator.Validate(request);
        if (!outcome.IsValid)
        {
            return Results.Json(new ErrorResponse("VALIDATION_FAILED", outcome.Errors),
                statusCode: 400);
        }

        var fingerprint = SHA256.HashData(body);
        var key = ReadIdempotencyKey(http, fingerprint);
        if (key is null)
        {
            return Results.Json(new ErrorResponse("INVALID_IDEMPOTENCY_KEY"), statusCode: 400);
        }

        var market = outcome.Market!;
        var submission = await submitter.SubmitAsync(
            new SubmissionRequest(market, outcome.Applicant!, outcome.Documents!, key,
                fingerprint),
            cancellationToken);

        switch (submission.Outcome)
        {
            case SubmissionOutcome.IdempotencyKeyReused:
                return Results.Json(new ErrorResponse("IDEMPOTENCY_KEY_REUSED"), statusCode: 409);
            case SubmissionOutcome.ApplicantAlreadyActive:
                // Deliberately gives no application id: that would tell the caller that someone
                // with this identifier has applied.
                return Results.Json(new ErrorResponse("APPLICATION_ALREADY_EXISTS"),
                    statusCode: 409);
        }

        var wait = TimeSpan.FromSeconds(options.Value.SyncWaitSeconds);
        var snapshot = await reader.WaitForOutcomeAsync(market, submission.ApplicationId!.Value,
            wait, cancellationToken);
        if (snapshot!.IsRestricted)
        {
            // A repeat of a request whose data the applicant has since asked to erase.
            return Results.Json(new ErrorResponse("APPLICATION_RESTRICTED"), statusCode: 409);
        }

        return Respond(http.HttpContext.Response, snapshot!, tokens.Create(snapshot!.Id),
            isSubmission: true);
    }

    private static async Task<IResult> GetAsync(string id, HttpRequest http,
        MarketRegistry markets, ApplicationReader reader, StatusTokens tokens,
        CancellationToken cancellationToken)
    {
        // Unknown id, unknown market and wrong token all look the same, so that nothing can be
        // learned by probing.
        var parsed = ApplicationIds.Parse(id);
        var market = parsed is null ? null : markets.Find(parsed.Value.Market);
        if (parsed is null || market is null
            || !tokens.IsValid(parsed.Value.Id, http.Headers[STATUS_TOKEN_HEADER].ToString()))
        {
            return Results.Json(new ErrorResponse("NOT_FOUND"), statusCode: 404);
        }

        var snapshot = await reader.FindAsync(market, parsed.Value.Id, cancellationToken);
        if (snapshot is null || snapshot.IsRestricted)
        {
            return Results.Json(new ErrorResponse("NOT_FOUND"), statusCode: 404);
        }

        await reader.RecordStatusReadAsync(market, snapshot.Id, cancellationToken);
        return Respond(http.HttpContext.Response, snapshot, null, isSubmission: false);
    }

    // For a submission, 201 is the ticket's "decided" answer (approved or rejected) and 202 means
    // accepted but not final. A status query is always a plain 200.
    private static IResult Respond(HttpResponse response, ApplicationSnapshot snapshot,
        string? token, bool isSubmission)
    {
        var publicId = ApplicationIds.Format(snapshot.Market, snapshot.Id);
        var body = new ApplicationResponse(publicId, snapshot.PublicStatus,
            snapshot.RejectionReason, token);
        response.Headers.Location = $"/applications/{publicId}";

        var decided = snapshot.Status is ApplicationStatus.Approved or ApplicationStatus.Rejected;
        if (snapshot.Status is ApplicationStatus.Processing or ApplicationStatus.Fulfilling)
        {
            response.Headers.RetryAfter = RETRY_AFTER_SECONDS.ToString();
        }

        var statusCode = !isSubmission ? 200 : decided ? 201 : 202;
        return Results.Json(body, statusCode: statusCode);
    }

    private static SubmitApplicationRequest? Deserialize(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<SubmitApplicationRequest>(body, _Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The client's key if it sent one, otherwise one derived from the request itself, so that
    // even clients that know nothing about idempotency are protected from double-taps.
    private static string? ReadIdempotencyKey(HttpRequest http, byte[] fingerprint)
    {
        var sent = http.Headers[IDEMPOTENCY_HEADER].ToString().Trim();
        if (sent.Length == 0)
        {
            return "fp-" + Convert.ToHexString(fingerprint).ToLowerInvariant();
        }

        return sent.Length <= MAX_KEY_LENGTH && KeyPattern().IsMatch(sent) ? sent : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]+$", RegexOptions.None, 100)]
    private static partial Regex KeyPattern();
}
