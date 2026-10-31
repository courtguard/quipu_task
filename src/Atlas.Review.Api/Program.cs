using Atlas.Review.Api.Data;
using Atlas.Review.Api.Endpoints;
using Atlas.Review.Api.Security;
using Atlas.Shared.Markets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.WithProperty("Service", "atlas-review")
        .WriteTo.Console()
        .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341"));

    builder.Configuration.AddMarketsFile();
    builder.Services.AddMarkets(builder.Configuration);
    builder.Services.Configure<SqlOptions>(builder.Configuration.GetSection("Sql"));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<StaffCases>();

    // Staff sign in with the bank's identity provider; this service only checks the token.
    // Every request therefore carries a named person, which the access log records.
    var auth = builder.Configuration.GetSection("Auth");
    var signingKey = auth["SigningKey"]
                     ?? throw new InvalidOperationException("Auth:SigningKey is required.");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = auth["Issuer"],
                ValidAudience = auth["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(StaffEndpoints.OFFICERS, policy => policy
            .RequireRole(StaffRoles.COMPLIANCE_OFFICER).RequireClaim("sub"));
        options.AddPolicy(StaffEndpoints.BRANCH, policy => policy
            .RequireRole(StaffRoles.BRANCH_STAFF).RequireClaim("sub"));
        options.AddPolicy(StaffEndpoints.DATA_PROTECTION, policy => policy
            .RequireRole(StaffRoles.DATA_PROTECTION_OFFICER).RequireClaim("sub"));
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        // Gives Swagger UI an "Authorize" button, so staff calls can be tried from the page.
        var bearer = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "A staff token. Locally: POST http://localhost:5300/idp/token with "
                          + "{\"subject\":\"officer-1\",\"role\":\"compliance_officer\","
                          + "\"markets\":[\"MB\"]} and paste the accessToken here.",
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };
        options.AddSecurityDefinition("Bearer", bearer);
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [bearer] = Array.Empty<string>()
        });
    });

    var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/health", () => Results.Ok(new { service = "atlas-review", status = "ok" }));
    app.MapStaff();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Atlas.Review.Api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

namespace Atlas.Review.Api
{
    /// <summary>Lets the integration tests start the API in-process.</summary>
    public sealed class ReviewApiMarker
    {
    }
}
