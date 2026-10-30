using Atlas.Stubs.Scenarios;

namespace Atlas.Stubs.Endpoints;

/// <summary>
/// Stand-in for core banking account opening and card ordering. Both calls are idempotent on
/// the <c>Idempotency-Key</c> header: the same key always yields the same identifier.
/// </summary>
public static class CoreBankingEndpoints
{
    private const string IDEMPOTENCY_HEADER = "Idempotency-Key";

    /// <summary>A request to open an account.</summary>
    /// <param name="Market">The customer's market code.</param>
    /// <param name="Name">The customer's full name.</param>
    public sealed record OpenAccountRequest(string? Market, string? Name);

    /// <summary>A request to order a card for an account.</summary>
    /// <param name="AccountId">The account the card is for.</param>
    /// <param name="Name">The name to put on the card.</param>
    public sealed record OrderCardRequest(string? AccountId, string? Name);

    /// <summary>Maps <c>POST /corebanking/v1/accounts</c> and <c>/cards</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapCoreBanking(this IEndpointRouteBuilder app)
    {
        app.MapPost("/corebanking/v1/accounts", OpenAccountAsync);
        app.MapPost("/corebanking/v1/cards", OrderCardAsync);
    }

    private static async Task<IResult> OpenAccountAsync(HttpRequest http,
        OpenAccountRequest request, ScenarioStore store, ScenarioEngine engine,
        CancellationToken cancellationToken)
    {
        var key = http.Headers[IDEMPOTENCY_HEADER].ToString();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(
                new { error = $"{IDEMPOTENCY_HEADER} header and name are required" });
        }

        var outcome = await engine.RunAsync(store.Current.Accounts, "accounts", request.Name, key,
            cancellationToken);
        if (outcome.FailStatus is { } status)
        {
            return Results.Json(new { error = "injected failure" }, statusCode: status);
        }

        outcome.Body["accountId"] = "acc_" + ScenarioEngine.ShortId(key);
        return Results.Json(outcome.Body);
    }

    private static async Task<IResult> OrderCardAsync(HttpRequest http,
        OrderCardRequest request, ScenarioStore store, ScenarioEngine engine,
        CancellationToken cancellationToken)
    {
        var key = http.Headers[IDEMPOTENCY_HEADER].ToString();
        if (string.IsNullOrWhiteSpace(key)
            || string.IsNullOrWhiteSpace(request.AccountId)
            || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new
            {
                error = $"{IDEMPOTENCY_HEADER} header, accountId and name are required"
            });
        }

        var outcome = await engine.RunAsync(store.Current.Cards, "cards", request.Name, key,
            cancellationToken);
        if (outcome.FailStatus is { } status)
        {
            return Results.Json(new { error = "injected failure" }, statusCode: status);
        }

        outcome.Body["cardOrderId"] = "crd_" + ScenarioEngine.ShortId(key);
        return Results.Json(outcome.Body);
    }
}
