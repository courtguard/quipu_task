using Atlas.Stubs.Scenarios;

namespace Atlas.Stubs.Endpoints;

/// <summary>
/// Stand-in for Refinitiv World-Check sanctions and PEP screening (the shape pasted into
/// ATLAS-1).
/// </summary>
public static class WorldCheckEndpoints
{
    /// <summary>A request to screen a person.</summary>
    /// <param name="Name">The person's full name.</param>
    /// <param name="DateOfBirth">The date of birth, as yyyy-MM-dd.</param>
    /// <param name="Nationality">The nationality, as a code.</param>
    public sealed record ScreeningRequest(string? Name, string? DateOfBirth, string? Nationality);

    /// <summary>Maps <c>POST /worldcheck/v2/screen</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapWorldCheck(this IEndpointRouteBuilder app)
    {
        app.MapPost("/worldcheck/v2/screen", ScreenAsync);
    }

    private static async Task<IResult> ScreenAsync(ScreeningRequest request,
        ScenarioStore store, ScenarioEngine engine, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.DateOfBirth)
            || string.IsNullOrWhiteSpace(request.Nationality))
        {
            return Results.BadRequest(
                new { error = "name, dateOfBirth and nationality are required" });
        }

        var callKey = $"{request.Name}|{request.DateOfBirth}|{request.Nationality}";
        var outcome = await engine.RunAsync(store.Current.WorldCheck, "worldcheck",
            request.Name, callKey, cancellationToken);
        if (outcome.FailStatus is { } status)
        {
            return Results.Json(new { error = "injected failure" }, statusCode: status);
        }

        outcome.Body["caseId"] = "wc_" + ScenarioEngine.ShortId(callKey);
        return Results.Json(outcome.Body);
    }
}
