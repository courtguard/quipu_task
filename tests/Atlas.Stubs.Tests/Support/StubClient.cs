using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atlas.Stubs.Tests.Support;

/// <summary>Runs the stubs in-process and calls them like the Atlas services will.</summary>
public sealed class StubClient : IDisposable
{
    private readonly WebApplicationFactory<Program> _Factory;

    /// <summary>Starts the stubs, optionally with a custom scenarios file.</summary>
    /// <param name="scenariosFile">The file to use, or null for the shipped one.</param>
    public StubClient(string? scenariosFile = null)
    {
        _Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            if (scenariosFile is not null)
            {
                host.UseSetting("Scenarios:File", scenariosFile);
            }
        });
        Http = _Factory.CreateClient();
    }

    /// <summary>The client to call the stubs with.</summary>
    public HttpClient Http { get; }

    /// <summary>Encodes text as the base64 "image" a test sends.</summary>
    public static string Image(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// <summary>Calls IDNow with the given text as the document image.</summary>
    public Task<HttpResponseMessage> IdentifyAsync(string document, string selfie = "selfie",
        string documentType = "PASSPORT")
    {
        return Http.PostAsJsonAsync("/idnow/v1/identifications", new
        {
            documentType,
            documentImage = Image(document),
            selfieImage = Image(selfie)
        });
    }

    /// <summary>Calls World-Check for the given name.</summary>
    public Task<HttpResponseMessage> ScreenAsync(string name)
    {
        return Http.PostAsJsonAsync("/worldcheck/v2/screen", new
        {
            name,
            dateOfBirth = "1991-03-04",
            nationality = "MB"
        });
    }

    /// <summary>Calls core banking to open an account.</summary>
    public Task<HttpResponseMessage> OpenAccountAsync(string name, string? key)
    {
        return PostWithKeyAsync("/corebanking/v1/accounts", new { market = "MB", name }, key);
    }

    /// <summary>Calls core banking to order a card.</summary>
    public Task<HttpResponseMessage> OrderCardAsync(string name, string? key)
    {
        return PostWithKeyAsync("/corebanking/v1/cards",
            new { accountId = "acc_1", name }, key);
    }

    /// <summary>Reads the JSON body of a response.</summary>
    public static async Task<JsonObject> BodyAsync(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    /// <summary>Makes "fail the first N" scenarios start over.</summary>
    public async Task ResetAsync()
    {
        var response = await Http.PostAsync("/admin/reset", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public void Dispose()
    {
        Http.Dispose();
        _Factory.Dispose();
    }

    private Task<HttpResponseMessage> PostWithKeyAsync(string url, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return Http.SendAsync(request);
    }
}
