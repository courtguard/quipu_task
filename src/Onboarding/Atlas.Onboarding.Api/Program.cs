using Atlas.Onboarding.Api;
using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Domain.Identity;
using Atlas.Onboarding.Infrastructure;
using Atlas.Onboarding.Infrastructure.Security;
using Atlas.Shared.Markets;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logs go to the console and to Seq. Request bodies (images, personal data) are never logged.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.WithProperty("Service", "atlas-onboarding")
        .WriteTo.Console()
        .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341"));

    // Market differences are configuration; an invalid market fails the start-up, not a customer.
    builder.Configuration.AddMarketsFile();
    builder.Services.AddMarkets(builder.Configuration);
    builder.Services.AddOnboardingInfrastructure(builder.Configuration);

    // No market names a check-digit algorithm yet: Annex B does not define them.
    builder.Services.AddSingleton(new IdentifierValidator(Array.Empty<IChecksumAlgorithm>()));
    builder.Services.AddSingleton<SubmissionValidator>();

    // Two base64 images of a few MB each, plus the rest of the body.
    builder.WebHost.ConfigureKestrel(kestrel =>
        kestrel.Limits.MaxRequestBodySize = 40 * 1024 * 1024);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
        options.SchemaFilter<SubmitApplicationExample>());

    var app = builder.Build();

    // Missing keys and unknown check-digit algorithms stop the start-up instead of failing the
    // first customer.
    _ = app.Services.GetRequiredService<ApplicantHasher>();
    _ = app.Services.GetRequiredService<StatusTokens>();
    var problems = app.Services.GetRequiredService<IdentifierValidator>()
        .FindConfigurationProblems(app.Services.GetRequiredService<MarketRegistry>().All);
    if (problems.Count > 0)
    {
        throw new InvalidOperationException(string.Join("; ", problems));
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapGet("/health", () => Results.Ok(new { service = "atlas-onboarding", status = "ok" }));
    app.MapApplications();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Atlas.Onboarding.Api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

namespace Atlas.Onboarding.Api
{
    /// <summary>Lets the integration tests start the API in-process.</summary>
    public sealed class ApiMarker
    {
    }
}
