using Atlas.Stubs.Scenarios;

namespace Atlas.Stubs.Endpoints;

/// <summary>Operations for whoever is driving the stand-ins.</summary>
public static class AdminEndpoints
{
    /// <summary>Maps <c>GET /admin/scenarios</c> and <c>POST /admin/reset</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        // What markers exist, as currently loaded from the scenarios file.
        app.MapGet("/admin/scenarios", (ScenarioStore store) => Results.Json(store.Current));

        // Makes "fail the first N calls" scenarios start over.
        app.MapPost("/admin/reset", (ScenarioEngine engine) =>
        {
            engine.Reset();
            return Results.NoContent();
        });
    }
}
