using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.Stubs.Endpoints;

/// <summary>
/// Stand-in for the bank's identity provider, which issues staff their sign-in tokens. It
/// exists only so that the Review API can be tried locally: it signs whatever it is asked to,
/// and must never be deployed.
/// </summary>
public static class IdentityProviderEndpoints
{
    private static readonly string[] _Roles = { "compliance_officer", "branch_staff" };

    /// <summary>A request for a staff token.</summary>
    /// <param name="Subject">The staff member's identifier, for example "officer-mb-1".</param>
    /// <param name="Name">The staff member's display name.</param>
    /// <param name="Role">compliance_officer or branch_staff.</param>
    /// <param name="Markets">The markets the staff member works for.</param>
    /// <param name="LifetimeMinutes">Minutes the token is valid; negative for an expired one.</param>
    public sealed record TokenRequest(
        string? Subject,
        string? Name,
        string? Role,
        List<string>? Markets,
        int? LifetimeMinutes);

    /// <summary>Maps <c>POST /idp/token</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapIdentityProvider(this IEndpointRouteBuilder app)
    {
        app.MapPost("/idp/token", (TokenRequest request, IConfiguration configuration) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || !_Roles.Contains(request.Role)
                || request.Markets is not { Count: > 0 })
            {
                return Results.BadRequest(new
                {
                    error = "subject, a role (compliance_officer or branch_staff) and at least "
                            + "one market are required"
                });
            }

            var key = new SymmetricSecurityKey(
                Convert.FromBase64String(configuration["Idp:SigningKey"]!));
            var expires = DateTime.UtcNow.AddMinutes(request.LifetimeMinutes ?? 60);
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = configuration["Idp:Issuer"],
                Audience = configuration["Idp:Audience"],
                IssuedAt = expires.AddHours(-2),
                NotBefore = expires.AddHours(-2),
                Expires = expires,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = request.Subject,
                    ["name"] = request.Name ?? request.Subject,
                    ["role"] = request.Role!,
                    ["markets"] = request.Markets.Select(m => m.ToUpperInvariant()).ToArray()
                },
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            });

            return Results.Json(new { accessToken = token, tokenType = "Bearer", expires });
        });
    }
}
