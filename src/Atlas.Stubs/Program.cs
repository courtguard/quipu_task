using Atlas.Stubs.Endpoints;
using Atlas.Stubs.Scenarios;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The scenarios file is read from the project folder when run from source, so that editing it
// takes effect at once; otherwise from the copy next to the binaries. "Scenarios:File"
// overrides both.
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IWebHostEnvironment>();
    var fromSource = Path.Combine(environment.ContentRootPath, "scenarios.json");
    var path = configuration["Scenarios:File"]
               ?? (File.Exists(fromSource)
                   ? fromSource
                   : Path.Combine(AppContext.BaseDirectory, "scenarios.json"));
    return new ScenarioStore(path, services.GetRequiredService<ILogger<ScenarioStore>>());
});
builder.Services.AddSingleton<ScenarioEngine>();

var app = builder.Build();

// Load the scenarios now, so that a broken file stops the stand-ins from starting instead of
// failing the first call.
_ = app.Services.GetRequiredService<ScenarioStore>();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { service = "atlas-stubs", status = "ok" }));
app.MapIdNow();
app.MapWorldCheck();
app.MapCoreBanking();
app.MapIdentityProvider();
app.MapAdmin();

app.Run();

/// <summary>Exposes the entry point to the integration tests.</summary>
public partial class Program
{
}
