using System.Net;
using Atlas.Stubs.Tests.Support;

namespace Atlas.Stubs.Tests;

public sealed class CoreBankingStubTests : IDisposable
{
    private readonly StubClient _Stubs = new();

    public void Dispose() => _Stubs.Dispose();

    [Fact]
    public async Task Opens_an_account()
    {
        var response = await _Stubs.OpenAccountAsync("Ana Petrova", "app-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await StubClient.BodyAsync(response);
        Assert.StartsWith("acc_", (string?)body["accountId"]);
    }

    [Fact]
    public async Task The_same_idempotency_key_always_gives_the_same_account()
    {
        var first = await StubClient.BodyAsync(await _Stubs.OpenAccountAsync("Ana", "app-1"));
        var again = await StubClient.BodyAsync(await _Stubs.OpenAccountAsync("Ana", "app-1"));
        var other = await StubClient.BodyAsync(await _Stubs.OpenAccountAsync("Ana", "app-2"));

        Assert.Equal((string?)first["accountId"], (string?)again["accountId"]);
        Assert.NotEqual((string?)first["accountId"], (string?)other["accountId"]);
    }

    [Fact]
    public async Task The_same_idempotency_key_always_gives_the_same_card_order()
    {
        var first = await StubClient.BodyAsync(await _Stubs.OrderCardAsync("Ana", "app-1"));
        var again = await StubClient.BodyAsync(await _Stubs.OrderCardAsync("Ana", "app-1"));

        Assert.StartsWith("crd_", (string?)first["cardOrderId"]);
        Assert.Equal((string?)first["cardOrderId"], (string?)again["cardOrderId"]);
    }

    [Fact]
    public async Task An_idempotency_key_is_required()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _Stubs.OpenAccountAsync("Ana", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _Stubs.OrderCardAsync("Ana", null)).StatusCode);
    }

    [Fact]
    public async Task The_account_can_open_while_the_card_order_is_down()
    {
        // The case that decision 19 is about: a cleared customer whose card order fails.
        var account = await _Stubs.OpenAccountAsync("Ana Card-Down", "app-1");
        var card = await _Stubs.OrderCardAsync("Ana Card-Down", "app-1");

        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, card.StatusCode);
    }

    [Fact]
    public async Task A_flaky_card_order_recovers_on_retry_with_the_same_key()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await _Stubs.OrderCardAsync("Ana Card-Flaky", "app-1")).StatusCode);
        }

        Assert.Equal(
            new[]
            {
                HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.OK
            },
            statuses);
    }

    [Fact]
    public async Task An_account_that_is_down_answers_503()
    {
        var response = await _Stubs.OpenAccountAsync("Ana Account-Down", "app-1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
