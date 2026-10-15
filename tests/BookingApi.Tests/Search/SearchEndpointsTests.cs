using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApi.Application.Search;
using BookingApi.Application.Suppliers;
using BookingApi.Tests.Fakes;
using BookingApi.Tests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingApi.Tests.Search;

[Collection(PostgresCollection.Name)]
public class SearchEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateOnly SearchDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private readonly PostgresFixture _postgres;

    public SearchEndpointsTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Parallel_search_slow_AeroHub_is_TimedOut_and_SkyLink_results_are_still_returned()
    {
        // SkyLink answers after 1 s of the 2 s budget: called one after the other, it would only start
        // once AeroHub had used up the whole budget and would time out too.
        await using var factory = CreateFactory(
            timeoutSeconds: 2,
            new FakeFlightSupplier("AeroHub", "AH", TimeSpan.FromSeconds(30), [FakeOffer("AeroHub", UniqueReference(), "PC201", 200m, "USD")]),
            new FakeFlightSupplier("SkyLink", "SL", TimeSpan.FromSeconds(1), [FakeOffer("SkyLink", UniqueReference(), "TK785", 265.40m, "EUR")]));
        var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var result = await SearchAsync(client, SearchDate, adults: 1);
        stopwatch.Stop();

        Assert.Collection(result.Suppliers,
            aeroHub =>
            {
                Assert.Equal("AeroHub", aeroHub.Name);
                Assert.Equal(SupplierSearchStatus.TimedOut, aeroHub.Status);
                Assert.Equal(0, aeroHub.Count);
            },
            skyLink =>
            {
                Assert.Equal("SkyLink", skyLink.Name);
                Assert.Equal(SupplierSearchStatus.Ok, skyLink.Status);
                Assert.Equal(1, skyLink.Count);
            });
        var offer = Assert.Single(result.Offers);
        Assert.Equal("SkyLink", offer.Supplier);
        Assert.Equal("TK785", offer.FlightNumber);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Search took {stopwatch.Elapsed}, it should stop at the timeout.");

        await using var dbContext = _postgres.CreateDbContext();
        Assert.True(await dbContext.Offers.AnyAsync(saved => saved.Id == offer.OfferId));
    }

    [Fact]
    public async Task Search_failing_supplier_is_Failed_and_other_results_are_still_returned()
    {
        await using var factory = CreateFactory(
            timeoutSeconds: 5,
            new FakeFlightSupplier("AeroHub", "AH", TimeSpan.Zero, [], new HttpRequestException("Connection refused.")),
            new FakeFlightSupplier("SkyLink", "SL", TimeSpan.Zero, [FakeOffer("SkyLink", UniqueReference(), "TK785", 265.40m, "EUR")]));
        var client = factory.CreateClient();

        var result = await SearchAsync(client, SearchDate, adults: 1);

        Assert.Equal(SupplierSearchStatus.Failed, result.Suppliers.Single(supplier => supplier.Name == "AeroHub").Status);
        Assert.Equal(SupplierSearchStatus.Ok, result.Suppliers.Single(supplier => supplier.Name == "SkyLink").Status);
        Assert.Equal("SkyLink", Assert.Single(result.Offers).Supplier);
    }

    [Fact]
    public async Task Search_saves_returned_offers_with_supplier_reference_and_30_minute_expiry()
    {
        var aeroHubReference = UniqueReference();
        var skyLinkReference = UniqueReference();
        await using var factory = CreateFactory(
            timeoutSeconds: 5,
            new FakeFlightSupplier("AeroHub", "AH", TimeSpan.Zero, [FakeOffer("AeroHub", aeroHubReference, "TK785", 293.10m, "USD")]),
            new FakeFlightSupplier("SkyLink", "SL", TimeSpan.Zero, [FakeOffer("SkyLink", skyLinkReference, "TK785", 265.40m, "EUR")]));
        var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(SearchDate, adults: 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(skyLinkReference, body);
        Assert.DoesNotContain("supplierReference", body, StringComparison.OrdinalIgnoreCase);

        var result = JsonSerializer.Deserialize<SearchResultDto>(body, JsonOptions)!;
        var offer = Assert.Single(result.Offers);
        Assert.StartsWith("SL-", offer.OfferId);
        Assert.Equal(446_000, offer.PriceIqd);

        await using var dbContext = _postgres.CreateDbContext();
        var saved = await dbContext.Offers.SingleAsync(savedOffer => savedOffer.Id == offer.OfferId);
        Assert.Equal("SkyLink", saved.Supplier);
        Assert.Equal(skyLinkReference, saved.SupplierReference);
        Assert.Equal(446_000, saved.PriceIqd);
        Assert.Equal(265.40m, saved.OriginalPrice);
        Assert.Equal(2, saved.Adults);
        Assert.Equal(TimeSpan.FromMinutes(30), saved.ExpiresAt - saved.CreatedAt);
        Assert.True(saved.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(29));
        Assert.False(await dbContext.Offers.AnyAsync(savedOffer => savedOffer.SupplierReference == aeroHubReference));
    }

    [Fact]
    public async Task Search_for_today_is_accepted()
    {
        await using var factory = CreateFactory(
            timeoutSeconds: 5,
            new FakeFlightSupplier("AeroHub", "AH", TimeSpan.Zero, []),
            new FakeFlightSupplier("SkyLink", "SL", TimeSpan.Zero, []));
        var client = factory.CreateClient();

        var result = await SearchAsync(client, DateOnly.FromDateTime(DateTime.UtcNow), adults: 1);

        Assert.Empty(result.Offers);
        Assert.All(result.Suppliers, supplier => Assert.Equal(SupplierSearchStatus.Ok, supplier.Status));
    }

    [Theory]
    [InlineData("from=bgw&to=IST&date={date}&adults=1")]
    [InlineData("from=BGW&to=ISTX&date={date}&adults=1")]
    [InlineData("to=IST&date={date}&adults=1")]
    [InlineData("from=BGW&to=IST&adults=1")]
    [InlineData("from=BGW&to=IST&date=2020-01-01&adults=1")]
    [InlineData("from=BGW&to=IST&date=not-a-date&adults=1")]
    [InlineData("from=BGW&to=IST&date={date}")]
    [InlineData("from=BGW&to=IST&date={date}&adults=0")]
    [InlineData("from=BGW&to=IST&date={date}&adults=10")]
    public async Task Search_invalid_query_returns_400(string query)
    {
        var client = _postgres.ApiFactory.CreateClient();

        var response = await client.GetAsync("/search?" + query.Replace("{date}", FormatDate(SearchDate)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(int timeoutSeconds, params FakeFlightSupplier[] suppliers) =>
        _postgres.ApiFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:TimeoutSeconds"] = timeoutSeconds.ToString(CultureInfo.InvariantCulture)
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFlightSupplier>();
                foreach (var supplier in suppliers)
                {
                    services.AddSingleton<IFlightSupplier>(supplier);
                }
            });
        });

    private static async Task<SearchResultDto> SearchAsync(HttpClient client, DateOnly date, int adults)
    {
        var response = await client.GetAsync(SearchUrl(date, adults));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchResultDto>(JsonOptions);
        Assert.NotNull(result);
        return result;
    }

    private static string SearchUrl(DateOnly date, int adults) =>
        $"/search?from=BGW&to=IST&date={FormatDate(date)}&adults={adults}";

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SupplierOffer FakeOffer(string supplier, string supplierReference, string flightNumber, decimal price, string currency) =>
        new(
            Supplier: supplier,
            SupplierReference: supplierReference,
            Airline: flightNumber[..2],
            FlightNumber: flightNumber,
            From: "BGW",
            To: "IST",
            Departure: SearchDate.ToDateTime(new TimeOnly(8, 30)),
            Arrival: SearchDate.ToDateTime(new TimeOnly(11, 0)),
            Stops: 0,
            OriginalPrice: price,
            OriginalCurrency: currency);

    private static string UniqueReference() => $"ref-{Guid.NewGuid():N}";
}
