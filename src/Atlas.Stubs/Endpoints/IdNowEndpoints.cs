using System.Security.Cryptography;
using System.Text;
using Atlas.Stubs.Scenarios;

namespace Atlas.Stubs.Endpoints;

/// <summary>
/// Stand-in for IDNow document authenticity and face match (the shape pasted into ATLAS-1).
/// </summary>
public static class IdNowEndpoints
{
    private static readonly string[] _DocumentTypes = { "PASSPORT", "ID_CARD" };

    /// <summary>A request to identify a person from a document and a selfie.</summary>
    /// <param name="DocumentType">PASSPORT or ID_CARD.</param>
    /// <param name="DocumentImage">The document photograph, base64 encoded.</param>
    /// <param name="SelfieImage">The selfie, base64 encoded.</param>
    public sealed record IdentificationRequest(
        string? DocumentType,
        string? DocumentImage,
        string? SelfieImage);

    /// <summary>Maps <c>POST /idnow/v1/identifications</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void MapIdNow(this IEndpointRouteBuilder app)
    {
        app.MapPost("/idnow/v1/identifications", IdentifyAsync);
    }

    private static async Task<IResult> IdentifyAsync(IdentificationRequest request,
        ScenarioStore store, ScenarioEngine engine, CancellationToken cancellationToken)
    {
        if (!_DocumentTypes.Contains(request.DocumentType))
        {
            return Results.BadRequest(new { error = "documentType must be PASSPORT or ID_CARD" });
        }

        var document = Decode(request.DocumentImage);
        var selfie = Decode(request.SelfieImage);
        if (document is null || selfie is null)
        {
            return Results.BadRequest(new { error = "images must be non-empty base64" });
        }

        // The markers can sit in either image. Latin-1 maps every byte to a character, so real
        // binary images are searched without loss and simply contain no marker.
        var trigger = Encoding.Latin1.GetString(document) + Encoding.Latin1.GetString(selfie);
        var callKey = Convert.ToHexString(SHA256.HashData(document));

        var outcome = await engine.RunAsync(store.Current.IdNow, "idnow", trigger, callKey,
            cancellationToken);
        if (outcome.FailStatus is { } status)
        {
            return Results.Json(new { error = "injected failure" }, statusCode: status);
        }

        outcome.Body["identificationId"] = "idn_" + ScenarioEngine.ShortId(callKey);
        return Results.Json(outcome.Body);
    }

    private static byte[]? Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
