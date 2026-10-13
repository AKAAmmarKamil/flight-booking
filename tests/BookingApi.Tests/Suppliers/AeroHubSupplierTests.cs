using System.Net;
using System.Text;
using BookingApi.Application.Suppliers;
using BookingApi.Infrastructure.Suppliers.AeroHub;
using BookingApi.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace BookingApi.Tests.Suppliers;

public class AeroHubSupplierTests
{
    private const string SpecSearchResponse = """
        { "results": [ {
          "offer_token": "aeh_91c2d4", "carrier_code": "TK", "flight_no": "785",
          "dep_airport": "BGW", "arr_airport": "IST",
          "dep_time": "2026-11-20T08:30:00", "arr_time": "2026-11-20T11:00:00",
          "stops": 0, "total_usd": 293.10 } ] }
        """;

    private static readonly SearchRequest BgwToIst = new("BGW", "IST", new DateOnly(2026, 11, 20), 1);

    private static readonly Passenger AliHassan = new("ALI", "HASSAN", new DateOnly(1990, 1, 15));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TokenResponse(string token) =>
        Json($$"""{ "access_token": "{{token}}", "expires_in": 600 }""");

    private static AeroHubSupplier CreateSupplier(StubHttpMessageHandler handler, int bookTimeoutSeconds = 15) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://aerohub.test") },
            new AeroHubTokenCache(TimeProvider.System),
            Options.Create(new AeroHubOptions
            {
                BaseUrl = "http://aerohub.test",
                Username = "flamingo",
                Password = "secret",
                BookTimeoutSeconds = bookTimeoutSeconds
            }));

    private static StubHttpMessageHandler HandlerAnsweringBookWith(Func<CancellationToken, Task<HttpResponseMessage>> bookResponse) =>
        new((request, cancellationToken) => request.RequestUri!.AbsolutePath switch
        {
            "/token" => Task.FromResult(TokenResponse("token-1")),
            "/book" => bookResponse(cancellationToken),
            _ => throw new InvalidOperationException($"Unexpected call to {request.RequestUri}")
        });

    private static SupplierOffer Tk785Offer() =>
        new("AeroHub", "aeh_91c2d4", "TK", "TK785", "BGW", "IST",
            new DateTime(2026, 11, 20, 8, 30, 0), new DateTime(2026, 11, 20, 11, 0, 0), 0, 293.10m, "USD");

    [Fact]
    public async Task Mapping_AeroHub_sample_maps_to_common_model_with_TK_785_as_TK785()
    {
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/token" ? TokenResponse("token-1") : Json(SpecSearchResponse)));

        var offers = await CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None);

        var offer = Assert.Single(offers);
        Assert.Equal("AeroHub", offer.Supplier);
        Assert.Equal("aeh_91c2d4", offer.SupplierReference);
        Assert.Equal("TK", offer.Airline);
        Assert.Equal("TK785", offer.FlightNumber);
        Assert.Equal("BGW", offer.From);
        Assert.Equal("IST", offer.To);
        Assert.Equal(new DateTime(2026, 11, 20, 8, 30, 0), offer.Departure);
        Assert.Equal(new DateTime(2026, 11, 20, 11, 0, 0), offer.Arrival);
        Assert.Equal(0, offer.Stops);
        Assert.Equal(293.10m, offer.OriginalPrice);
        Assert.Equal("USD", offer.OriginalCurrency);
    }

    [Fact]
    public async Task TokenExpiry_one_401_then_one_new_token_and_one_successful_retry()
    {
        var issuedTokens = 0;
        var searchCalls = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                issuedTokens++;
                return Task.FromResult(TokenResponse($"token-{issuedTokens}"));
            }

            searchCalls++;
            return Task.FromResult(searchCalls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(SpecSearchResponse));
        });

        var offers = await CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None);

        Assert.Single(offers);
        Assert.Equal(2, handler.CallsTo("/token"));
        Assert.Equal(2, handler.CallsTo("/search"));
        var searchRequests = handler.Requests.Where(request => request.RequestUri!.AbsolutePath == "/search").ToList();
        Assert.Equal("token-1", searchRequests[0].Headers.Authorization!.Parameter);
        Assert.Equal("token-2", searchRequests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task TokenCache_reuses_valid_token_across_calls()
    {
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/token" ? TokenResponse("token-1") : Json(SpecSearchResponse)));
        var supplier = CreateSupplier(handler);

        await supplier.SearchAsync(BgwToIst, CancellationToken.None);
        await supplier.SearchAsync(BgwToIst, CancellationToken.None);

        Assert.Equal(1, handler.CallsTo("/token"));
        Assert.Equal(2, handler.CallsTo("/search"));
    }

    [Fact]
    public async Task BookOutcome_AeroHub_CONFIRMED_is_Confirmed_with_pnr_and_ticket()
    {
        var handler = HandlerAnsweringBookWith(_ => Task.FromResult(
            Json("""{ "status": "CONFIRMED", "pnr": "X7K2LM", "ticket_number": "2351234567890" }""")));

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Confirmed, result.Outcome);
        Assert.Equal("X7K2LM", result.Pnr);
        Assert.Equal("2351234567890", result.TicketNumber);
    }

    [Fact]
    public async Task BookOutcome_AeroHub_FAILED_is_Refused_with_reason()
    {
        var handler = HandlerAnsweringBookWith(_ => Task.FromResult(
            Json("""{ "status": "FAILED", "reason": "SOLD_OUT" }""")));

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Refused, result.Outcome);
        Assert.Contains("SOLD_OUT", result.Note);
    }

    [Fact]
    public async Task BookOutcome_AeroHub_timeout_is_Unknown_with_may_exist_note()
    {
        var handler = HandlerAnsweringBookWith(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        var result = await CreateSupplier(handler, bookTimeoutSeconds: 1).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Unknown, result.Outcome);
        Assert.Contains("may have been issued", result.Note);
        Assert.Equal(1, handler.CallsTo("/book"));
    }
}
