using System.Net;
using System.Text;
using System.Text.Json;
using BookingApi.Application.Suppliers;
using BookingApi.Infrastructure.Suppliers.SkyLink;
using BookingApi.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingApi.Tests.Suppliers;

public class SkyLinkSupplierTests
{
    private const string SpecShoppingResponse = """
        { "errorCode": "0", "data": {
          "solutions": [ { "solutionId": "S1", "flightIds": ["F1"], "priceEur": 265.40 } ],
          "flights":   [ { "flightId": "F1", "segmentIds": ["G1"] } ],
          "segments":  [ { "segmentId": "G1", "airline": "TK", "flightNum": "785",
                           "dep": "BGW", "arr": "IST",
                           "depDate": "2026-11-20", "depTime": "08:30",
                           "arrDate": "2026-11-20", "arrTime": "11:00" } ] } }
        """;

    private const string TwoSegmentShoppingResponse = """
        { "errorCode": "0", "data": {
          "solutions": [ { "solutionId": "S2", "flightIds": ["F1"], "priceEur": 198.00 } ],
          "flights":   [ { "flightId": "F1", "segmentIds": ["G1", "G2"] } ],
          "segments":  [ { "segmentId": "G1", "airline": "RJ", "flightNum": "411",
                           "dep": "BGW", "arr": "AMM",
                           "depDate": "2026-11-20", "depTime": "23:10",
                           "arrDate": "2026-11-21", "arrTime": "00:40" },
                         { "segmentId": "G2", "airline": "RJ", "flightNum": "165",
                           "dep": "AMM", "arr": "IST",
                           "depDate": "2026-11-21", "depTime": "02:15",
                           "arrDate": "2026-11-21", "arrTime": "05:20" } ] } }
        """;

    private const string OrderCreated = """{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "TO_BE_PAID" } }""";

    private static readonly SearchRequest BgwToIst = new("BGW", "IST", new DateOnly(2026, 11, 20), 1);

    private static readonly Passenger AliHassan = new("ALI", "HASSAN", new DateOnly(1990, 1, 15));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SkyLinkSupplier CreateSupplier(StubHttpMessageHandler handler, int bookTimeoutSeconds = 15) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://skylink.test") },
            Options.Create(new SkyLinkOptions
            {
                BaseUrl = "http://skylink.test",
                PartnerId = "FLM001",
                PartnerKey = "k3y-123",
                BookTimeoutSeconds = bookTimeoutSeconds
            }),
            NullLogger<SkyLinkSupplier>.Instance);

    private static StubHttpMessageHandler HandlerAnswering(string path, string body) =>
        new((request, _) => request.RequestUri!.AbsolutePath == path
            ? Task.FromResult(Json(body))
            : throw new InvalidOperationException($"Unexpected call to {request.RequestUri}"));

    private static StubHttpMessageHandler HandlerAnsweringBookWith(string orderBody, Func<CancellationToken, Task<HttpResponseMessage>> ticketingResponse) =>
        new((request, cancellationToken) => request.RequestUri!.AbsolutePath switch
        {
            "/order" => Task.FromResult(Json(orderBody)),
            "/ticketing" => ticketingResponse(cancellationToken),
            _ => throw new InvalidOperationException($"Unexpected call to {request.RequestUri}")
        });

    private static SupplierOffer Tk785Offer() =>
        new("SkyLink", "S1", "TK", "TK785", "BGW", "IST",
            new DateTime(2026, 11, 20, 8, 30, 0), new DateTime(2026, 11, 20, 11, 0, 0), 0, 265.40m, "EUR");

    [Fact]
    public async Task Mapping_SkyLink_sample_joins_solutions_flights_segments_into_common_model()
    {
        var handler = HandlerAnswering("/shopping", SpecShoppingResponse);

        var offers = await CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None);

        var offer = Assert.Single(offers);
        Assert.Equal("SkyLink", offer.Supplier);
        Assert.Equal("S1", offer.SupplierReference);
        Assert.Equal("TK", offer.Airline);
        Assert.Equal("TK785", offer.FlightNumber);
        Assert.Equal("BGW", offer.From);
        Assert.Equal("IST", offer.To);
        Assert.Equal(new DateTime(2026, 11, 20, 8, 30, 0), offer.Departure);
        Assert.Equal(new DateTime(2026, 11, 20, 11, 0, 0), offer.Arrival);
        Assert.Equal(0, offer.Stops);
        Assert.Equal(265.40m, offer.OriginalPrice);
        Assert.Equal("EUR", offer.OriginalCurrency);
    }

    [Fact]
    public async Task Mapping_SkyLink_two_segment_flight_has_one_stop_first_departure_and_last_arrival()
    {
        var handler = HandlerAnswering("/shopping", TwoSegmentShoppingResponse);

        var offers = await CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None);

        var offer = Assert.Single(offers);
        Assert.Equal(1, offer.Stops);
        Assert.Equal("RJ411", offer.FlightNumber);
        Assert.Equal("BGW", offer.From);
        Assert.Equal("IST", offer.To);
        Assert.Equal(new DateTime(2026, 11, 20, 23, 10, 0), offer.Departure);
        Assert.Equal(new DateTime(2026, 11, 21, 5, 20, 0), offer.Arrival);
    }

    [Fact]
    public async Task Auth_request_carries_partnerId_and_md5_sign_but_never_the_partnerKey()
    {
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            sentBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(SpecShoppingResponse);
        });

        await CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None);

        using var document = JsonDocument.Parse(sentBody!);
        var auth = document.RootElement.GetProperty("auth");
        Assert.Equal("FLM001", auth.GetProperty("partnerId").GetString());
        Assert.Equal("1bd13525a5637e9a90a8c416f16628b9", auth.GetProperty("sign").GetString());
        Assert.DoesNotContain("k3y-123", sentBody);
    }

    [Fact]
    public async Task Search_nonzero_errorCode_throws_although_HTTP_status_is_200()
    {
        var handler = HandlerAnswering("/shopping", """{ "errorCode": "A001", "errorMsg": "invalid sign" }""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSupplier(handler).SearchAsync(BgwToIst, CancellationToken.None));

        Assert.Contains("A001", exception.Message);
    }

    [Fact]
    public async Task BookOutcome_SkyLink_ISSUING_is_Pending_with_orderNum_and_buyerOrder_is_bookingId()
    {
        string? orderBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/order")
            {
                orderBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(OrderCreated);
            }

            return Json("""{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "ISSUING" } }""");
        });

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Pending, result.Outcome);
        Assert.Equal("917000000001", result.SupplierReference);
        Assert.Equal("SkyLink order 917000000001 is ISSUING", result.Note);
        using var document = JsonDocument.Parse(orderBody!);
        Assert.Equal("booking-1", document.RootElement.GetProperty("buyerOrder").GetString());
        Assert.Equal("S1", document.RootElement.GetProperty("solutionId").GetString());
    }

    [Fact]
    public async Task BookOutcome_SkyLink_B029_at_order_is_Unknown_and_ticketing_is_never_called()
    {
        var handler = HandlerAnsweringBookWith(
            """{ "errorCode": "B029", "errorMsg": "duplicate order" }""",
            _ => throw new InvalidOperationException("ticketing must not be called"));

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Unknown, result.Outcome);
        Assert.Contains("B029", result.Note);
        Assert.Equal(0, handler.CallsTo("/ticketing"));
        Assert.Contains("may have been issued", result.Note);
        Assert.Contains("booking-1", result.Note);
    }

    [Fact]
    public async Task BookOutcome_SkyLink_B050_at_order_is_Refused_and_ticketing_is_never_called()
    {
        var handler = HandlerAnsweringBookWith(
            """{ "errorCode": "B050", "errorMsg": "fare no longer available" }""",
            _ => throw new InvalidOperationException("ticketing must not be called"));

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Refused, result.Outcome);
        Assert.Contains("B050", result.Note);
        Assert.Equal(0, handler.CallsTo("/ticketing"));
    }

    [Fact]
    public async Task BookOutcome_SkyLink_B022_at_ticketing_is_Refused_and_keeps_orderNum()
    {
        var handler = HandlerAnsweringBookWith(OrderCreated, _ => Task.FromResult(
            Json("""{ "errorCode": "B022", "errorMsg": "prepaid balance too low" }""")));

        var result = await CreateSupplier(handler).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Refused, result.Outcome);
        Assert.Equal("917000000001", result.SupplierReference);
        Assert.Contains("B022", result.Note);
    }

    [Fact]
    public async Task BookOutcome_SkyLink_ticketing_timeout_is_Unknown_with_may_exist_note()
    {
        var handler = HandlerAnsweringBookWith(OrderCreated, async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        var result = await CreateSupplier(handler, bookTimeoutSeconds: 1).BookAsync(Tk785Offer(), AliHassan, "booking-1", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Unknown, result.Outcome);
        Assert.Equal("917000000001", result.SupplierReference);
        Assert.Contains("may have been issued", result.Note);
    }

    [Fact]
    public async Task GetStatus_SkyLink_ISSUED_is_Confirmed_with_pnr_and_ticket()
    {
        var handler = HandlerAnswering("/order/917000000001",
            """{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "ISSUED", "pnr": "Q8W2ER", "ticketNumber": "6071234567890" } }""");

        var result = await CreateSupplier(handler).GetStatusAsync("917000000001", CancellationToken.None);

        Assert.Equal(SupplierBookingOutcome.Confirmed, result.Outcome);
        Assert.Equal("Q8W2ER", result.Pnr);
        Assert.Equal("6071234567890", result.TicketNumber);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }
}
