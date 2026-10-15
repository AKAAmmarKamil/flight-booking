using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApi.Application.Wallets;
using BookingApi.Domain;
using BookingApi.Tests.Persistence;

namespace BookingApi.Tests.Wallets;

[Collection(PostgresCollection.Name)]
public class WalletEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public WalletEndpointsTests(PostgresFixture postgres)
    {
        _client = postgres.ApiFactory.CreateClient();
    }

    [Fact]
    public async Task Wallet_first_deposit_creates_wallet_and_returns_balance()
    {
        var userId = UniqueUserId();

        var response = await DepositAsync(userId, 1_000_000);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DepositResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(1_000_000, result.BalanceIqd);
    }

    [Fact]
    public async Task Wallet_deposits_accumulate_and_are_listed_in_order()
    {
        var userId = UniqueUserId();
        await DepositAsync(userId, 1_000_000);
        await DepositAsync(userId, 250_000);

        var wallet = await GetWalletAsync(userId);

        Assert.Equal(1_250_000, wallet.BalanceIqd);
        Assert.Collection(wallet.Transactions,
            first =>
            {
                Assert.Equal(WalletTransactionType.Deposit, first.Type);
                Assert.Equal(1_000_000, first.AmountIqd);
            },
            second =>
            {
                Assert.Equal(WalletTransactionType.Deposit, second.Type);
                Assert.Equal(250_000, second.AmountIqd);
            });
        Assert.True(wallet.Transactions[0].At <= wallet.Transactions[1].At);
    }

    [Fact]
    public async Task Wallet_unknown_user_returns_404()
    {
        var response = await _client.GetAsync($"/wallets/{UniqueUserId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"amountIqd":0}""")]
    [InlineData("""{"amountIqd":-5}""")]
    [InlineData("""{"amountIqd":1000.5}""")]
    [InlineData("""{"amountIqd":1000000001}""")]
    [InlineData("""{"amountIqd":5000000000}""")]
    [InlineData("""{"amountIqd":null}""")]
    [InlineData("{}")]
    public async Task Wallet_deposit_with_invalid_amount_returns_400_and_creates_no_wallet(string body)
    {
        var userId = UniqueUserId();

        var response = await _client.PostAsync($"/wallets/{userId}/deposit", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var walletResponse = await _client.GetAsync($"/wallets/{userId}");
        Assert.Equal(HttpStatusCode.NotFound, walletResponse.StatusCode);
    }

    [Fact]
    public async Task Wallet_concurrent_first_deposits_lose_nothing()
    {
        const int depositCount = 20;
        var userId = UniqueUserId();

        var responses = await Task.WhenAll(Enumerable.Range(0, depositCount).Select(_ => DepositAsync(userId, 1_000)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var returnedBalances = new List<long>();
        foreach (var response in responses)
        {
            var result = await response.Content.ReadFromJsonAsync<DepositResultDto>(JsonOptions);
            returnedBalances.Add(result!.BalanceIqd);
        }

        var expectedBalances = Enumerable.Range(1, depositCount).Select(depositNumber => depositNumber * 1_000L);
        Assert.Equal(expectedBalances, returnedBalances.Order());

        var wallet = await GetWalletAsync(userId);
        Assert.Equal(depositCount * 1_000, wallet.BalanceIqd);
        Assert.Equal(depositCount, wallet.Transactions.Count);
    }

    [Fact]
    public async Task Wallet_overlong_user_id_returns_400()
    {
        var userId = new string('u', ColumnLengths.UserId + 1);

        var depositResponse = await DepositAsync(userId, 1_000);
        var getResponse = await _client.GetAsync($"/wallets/{userId}");

        Assert.Equal(HttpStatusCode.BadRequest, depositResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, getResponse.StatusCode);
    }

    private Task<HttpResponseMessage> DepositAsync(string userId, long amountIqd)
    {
        return _client.PostAsJsonAsync($"/wallets/{userId}/deposit", new DepositForm { AmountIqd = amountIqd });
    }

    private async Task<WalletDto> GetWalletAsync(string userId)
    {
        var wallet = await _client.GetFromJsonAsync<WalletDto>($"/wallets/{userId}", JsonOptions);
        Assert.NotNull(wallet);
        return wallet;
    }

    private static string UniqueUserId()
    {
        return $"user-{Guid.NewGuid():N}";
    }
}
