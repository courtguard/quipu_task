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

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGet("/health", () => Results.Ok(new { service = "atlas-onboarding", status = "ok" }));

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Atlas.Onboarding.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
