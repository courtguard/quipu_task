using System.Net.Http.Json;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;
using Atlas.Shared.Markets;
using Microsoft.Extensions.Logging;
using Polly;

namespace Atlas.Onboarding.Infrastructure.Providers;

/// <summary>
/// Shared handling of calls to outside providers: anything that stops an answer arriving is
/// reported as "unavailable" and never thrown into the workflow.
/// </summary>
internal static class ProviderCalls
{
    public static async Task<ProviderResult<T>> SendAsync<T>(ILogger logger, string provider,
        Func<Task<ProviderResult<T>>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                                   && (ex is HttpRequestException
                                       or ExecutionRejectedException
                                       or TaskCanceledException
                                       or System.Text.Json.JsonException))
        {
            // Timeouts, open circuit breakers, connection failures and unreadable answers all
            // mean the same to the workflow: try again later.
            logger.LogWarning("{Provider} gave no answer: {Kind}", provider, ex.GetType().Name);
            return ProviderResult<T>.Unavailable($"{provider}: {ex.GetType().Name}");
        }
    }

    public static ProviderResult<T> Failed<T>(ILogger logger, string provider,
        HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500 and not 408 and not 429)
        {
            // Our request was refused. Retrying will not help, but it is not the applicant's
            // fault either, so it is handled like an outage and surfaces through expiry.
            logger.LogError("{Provider} refused the request with HTTP {Status}", provider, status);
        }
        else
        {
            logger.LogWarning("{Provider} failed with HTTP {Status}", provider, status);
        }

        return ProviderResult<T>.Unavailable($"{provider}: HTTP {status}");
    }
}

/// <summary>Calls IDNow for document authenticity and face match.</summary>
public sealed class HttpIdentityVerifier : IIdentityVerifier
{
    private const string PROVIDER = "idnow";

    private readonly HttpClient _Http;
    private readonly ILogger<HttpIdentityVerifier> _Logger;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The configured HTTP client, with its retries and timeouts.</param>
    /// <param name="logger">Where problems are reported.</param>
    public HttpIdentityVerifier(HttpClient http, ILogger<HttpIdentityVerifier> logger)
    {
        _Http = http;
        _Logger = logger;
    }

    private sealed record Response(
        string? IdentificationId,
        string? DocumentResult,
        bool? FaceMatch,
        double? Confidence);

    /// <inheritdoc />
    public Task<ProviderResult<IdentityVerification>> VerifyAsync(MarketProfile market,
        DocumentKind kind, byte[] document, byte[] selfie, CancellationToken cancellationToken)
    {
        return ProviderCalls.SendAsync(_Logger, PROVIDER, async () =>
        {
            var uri = new Uri(new Uri(market.IdNowBaseUrl), "v1/identifications");
            var body = new
            {
                documentType = kind == DocumentKind.Passport ? "PASSPORT" : "ID_CARD",
                documentImage = Convert.ToBase64String(document),
                selfieImage = Convert.ToBase64String(selfie)
            };

            using var response = await _Http.PostAsJsonAsync(uri, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderCalls.Failed<IdentityVerification>(_Logger, PROVIDER, response);
            }

            var answer = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
            return Parse(answer);
        }, cancellationToken);
    }

    private ProviderResult<IdentityVerification> Parse(Response? answer)
    {
        var document = answer?.DocumentResult switch
        {
            "VALID" => DocumentResult.Valid,
            "INVALID" => DocumentResult.Invalid,
            "INCONCLUSIVE" => DocumentResult.Inconclusive,
            _ => (DocumentResult?)null
        };

        if (answer is null || document is null || answer.FaceMatch is null
            || answer.Confidence is null || string.IsNullOrWhiteSpace(answer.IdentificationId))
        {
            _Logger.LogError("{Provider} answered with an unexpected shape", PROVIDER);
            return ProviderResult<IdentityVerification>.Unavailable($"{PROVIDER}: bad answer");
        }

        var check = new IdentityCheck(document.Value, answer.FaceMatch.Value,
            answer.Confidence.Value);
        return ProviderResult<IdentityVerification>.Success(
            new IdentityVerification(check, answer.IdentificationId));
    }
}

/// <summary>Calls World-Check for sanctions and PEP screening.</summary>
public sealed class HttpScreeningService : IScreeningService
{
    private const string PROVIDER = "worldcheck";

    private readonly HttpClient _Http;
    private readonly ILogger<HttpScreeningService> _Logger;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The configured HTTP client, with its retries and timeouts.</param>
    /// <param name="logger">Where problems are reported.</param>
    public HttpScreeningService(HttpClient http, ILogger<HttpScreeningService> logger)
    {
        _Http = http;
        _Logger = logger;
    }

    private sealed record Response(string? CaseId, string? Status);

    /// <inheritdoc />
    public Task<ProviderResult<ScreeningOutcome>> ScreenAsync(MarketProfile market,
        ApplicantDetails applicant, CancellationToken cancellationToken)
    {
        return ProviderCalls.SendAsync(_Logger, PROVIDER, async () =>
        {
            var uri = new Uri(new Uri(market.WorldCheckBaseUrl), "v2/screen");
            var body = new
            {
                name = applicant.FullName,
                dateOfBirth = applicant.DateOfBirth,
                nationality = applicant.Nationality
            };

            using var response = await _Http.PostAsJsonAsync(uri, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderCalls.Failed<ScreeningOutcome>(_Logger, PROVIDER, response);
            }

            var answer = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
            return Parse(answer);
        }, cancellationToken);
    }

    private ProviderResult<ScreeningOutcome> Parse(Response? answer)
    {
        var status = answer?.Status switch
        {
            "CLEAR" => ScreeningStatus.Clear,
            "POSSIBLE_MATCH" => ScreeningStatus.PossibleMatch,
            _ => (ScreeningStatus?)null
        };

        if (answer is null || status is null || string.IsNullOrWhiteSpace(answer.CaseId))
        {
            _Logger.LogError("{Provider} answered with an unexpected shape", PROVIDER);
            return ProviderResult<ScreeningOutcome>.Unavailable($"{PROVIDER}: bad answer");
        }

        return ProviderResult<ScreeningOutcome>.Success(
            new ScreeningOutcome(new ScreeningCheck(status.Value), answer.CaseId));
    }
}

/// <summary>Calls core banking to open accounts and order cards.</summary>
public sealed class HttpCoreBanking : ICoreBanking
{
    private const string PROVIDER = "corebanking";
    private const string IDEMPOTENCY_HEADER = "Idempotency-Key";

    private readonly HttpClient _Http;
    private readonly ILogger<HttpCoreBanking> _Logger;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The configured HTTP client, with its retries and timeouts.</param>
    /// <param name="logger">Where problems are reported.</param>
    public HttpCoreBanking(HttpClient http, ILogger<HttpCoreBanking> logger)
    {
        _Http = http;
        _Logger = logger;
    }

    private sealed record AccountResponse(string? AccountId);

    private sealed record CardResponse(string? CardOrderId);

    /// <inheritdoc />
    public Task<ProviderResult<string>> OpenAccountAsync(MarketProfile market,
        string idempotencyKey, string holderName, CancellationToken cancellationToken)
    {
        return PostAsync<AccountResponse>(market, "v1/accounts", idempotencyKey,
            new { market = market.Code, name = holderName }, r => r.AccountId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProviderResult<string>> OrderCardAsync(MarketProfile market,
        string idempotencyKey, string accountId, string holderName,
        CancellationToken cancellationToken)
    {
        return PostAsync<CardResponse>(market, "v1/cards", idempotencyKey,
            new { accountId, name = holderName }, r => r.CardOrderId, cancellationToken);
    }

    private Task<ProviderResult<string>> PostAsync<TResponse>(MarketProfile market, string path,
        string idempotencyKey, object body, Func<TResponse, string?> pick,
        CancellationToken cancellationToken)
    {
        return ProviderCalls.SendAsync(_Logger, PROVIDER, async () =>
        {
            var uri = new Uri(new Uri(market.CoreBankingBaseUrl), path);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add(IDEMPOTENCY_HEADER, idempotencyKey);

            using var response = await _Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderCalls.Failed<string>(_Logger, PROVIDER, response);
            }

            var answer = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
            var id = answer is null ? null : pick(answer);
            if (string.IsNullOrWhiteSpace(id))
            {
                _Logger.LogError("{Provider} answered with an unexpected shape", PROVIDER);
                return ProviderResult<string>.Unavailable($"{PROVIDER}: bad answer");
            }

            return ProviderResult<string>.Success(id);
        }, cancellationToken);
    }
}
