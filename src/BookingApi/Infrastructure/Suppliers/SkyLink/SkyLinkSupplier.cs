using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BookingApi.Application.Suppliers;
using Microsoft.Extensions.Options;

namespace BookingApi.Infrastructure.Suppliers.SkyLink;

public sealed class SkyLinkSupplier : IFlightSupplier
{
    public const string SupplierName = "SkyLink";

    private const string SuccessCode = "0";
    private const string PrepaidBalanceTooLowCode = "B022";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SkyLinkOptions _options;
    private readonly ILogger<SkyLinkSupplier> _logger;
    private readonly SkyLinkAuth _auth;

    public SkyLinkSupplier(HttpClient httpClient, IOptions<SkyLinkOptions> options, ILogger<SkyLinkSupplier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _auth = new SkyLinkAuth(_options.PartnerId, SkyLinkSign.Compute(_options.PartnerId, _options.PartnerKey));
    }

    public string Name => SupplierName;

    public async Task<IReadOnlyList<SupplierOffer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var body = new SkyLinkShoppingRequest(_auth, new SkyLinkSearch(request.From, request.To, request.Date, request.Adults));

        var response = await PostAsync<SkyLinkShoppingRequest, SkyLinkShoppingData>("/shopping", body, cancellationToken);

        return ToSupplierOffers(GetDataOrThrow(response, "/shopping"));
    }

    public async Task<SupplierBookingResult> BookAsync(SupplierOffer offer, Passenger passenger, string bookingId, CancellationToken cancellationToken)
    {
        var orderBody = new SkyLinkOrderRequest(
            _auth,
            offer.SupplierReference,
            new SkyLinkPassenger(passenger.FirstName, passenger.LastName, passenger.BirthDate),
            bookingId);

        using var bookTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bookTimeout.CancelAfter(TimeSpan.FromSeconds(_options.BookTimeoutSeconds));

        string? orderNum = null;
        try
        {
            var order = await PostAsync<SkyLinkOrderRequest, SkyLinkOrderData>("/order", orderBody, bookTimeout.Token);
            if (order.ErrorCode != SuccessCode)
            {
                return SupplierBookingResult.Refused(
                    $"SkyLink refused the order ({order.ErrorCode} {order.ErrorMsg}): no ticket was issued.");
            }

            orderNum = order.Data!.OrderNum;

            var ticketing = await PostAsync<SkyLinkTicketingRequest, SkyLinkOrderData>(
                "/ticketing", new SkyLinkTicketingRequest(_auth, orderNum), bookTimeout.Token);

            return ticketing.ErrorCode switch
            {
                SuccessCode => ToBookingResult(ticketing.Data!),
                PrepaidBalanceTooLowCode => SupplierBookingResult.Refused(
                    $"SkyLink refused ticketing ({ticketing.ErrorCode} {ticketing.ErrorMsg}): order {orderNum} exists unpaid at SkyLink, no ticket was issued.",
                    orderNum),
                _ => SupplierBookingResult.Unknown(
                    $"SkyLink ticketing for order {orderNum} returned {ticketing.ErrorCode} {ticketing.ErrorMsg}: a ticket may have been issued; check with SkyLink.",
                    orderNum)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NotCompleted(orderNum, bookingId, $"did not answer within {_options.BookTimeoutSeconds} s");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return NotCompleted(orderNum, bookingId, $"call failed ({exception.GetType().Name})");
        }
    }

    public async Task<SupplierBookingResult> GetStatusAsync(string supplierReference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/order/{Uri.EscapeDataString(supplierReference)}", cancellationToken);
        var order = await ReadAsync<SkyLinkOrderData>(response, "/order", cancellationToken);

        return ToBookingResult(GetDataOrThrow(order, "/order"));
    }

    private IReadOnlyList<SupplierOffer> ToSupplierOffers(SkyLinkShoppingData data)
    {
        var flightsById = data.Flights.ToDictionary(flight => flight.FlightId);
        var segmentsById = data.Segments.ToDictionary(segment => segment.SegmentId);

        var offers = new List<SupplierOffer>();
        foreach (var solution in data.Solutions)
        {
            if (solution.FlightIds.Count != 1)
            {
                _logger.LogWarning(
                    "Skipping SkyLink solution {SolutionId}: it has {FlightCount} flights, only one-way solutions are supported.",
                    solution.SolutionId, solution.FlightIds.Count);
                continue;
            }

            var segments = flightsById[solution.FlightIds[0]].SegmentIds
                .Select(segmentId => segmentsById[segmentId])
                .ToList();
            offers.Add(ToSupplierOffer(solution, segments));
        }

        return offers;
    }

    private static SupplierOffer ToSupplierOffer(SkyLinkSolution solution, IReadOnlyList<SkyLinkSegment> segments)
    {
        var firstSegment = segments[0];
        var lastSegment = segments[^1];

        return new(
            Supplier: SupplierName,
            SupplierReference: solution.SolutionId,
            Airline: firstSegment.Airline,
            FlightNumber: firstSegment.Airline + firstSegment.FlightNum,
            From: firstSegment.Dep,
            To: lastSegment.Arr,
            Departure: ToDateTime(firstSegment.DepDate, firstSegment.DepTime),
            Arrival: ToDateTime(lastSegment.ArrDate, lastSegment.ArrTime),
            Stops: segments.Count - 1,
            OriginalPrice: solution.PriceEur,
            OriginalCurrency: "EUR");
    }

    private static DateTime ToDateTime(DateOnly date, string time) =>
        date.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));

    private static SupplierBookingResult ToBookingResult(SkyLinkOrderData order) =>
        order switch
        {
            { Status: "ISSUED", Pnr: { } pnr, TicketNumber: { } ticketNumber } =>
                SupplierBookingResult.Confirmed(order.OrderNum, pnr, ticketNumber, $"SkyLink order {order.OrderNum} is ISSUED."),
            { Status: "ISSUING" } =>
                SupplierBookingResult.Pending(order.OrderNum, $"SkyLink order {order.OrderNum} is ISSUING"),
            _ =>
                SupplierBookingResult.Unknown(
                    $"SkyLink order {order.OrderNum} has status {order.Status}: a ticket may have been issued; check with SkyLink.",
                    order.OrderNum)
        };

    private static SupplierBookingResult NotCompleted(string? orderNum, string bookingId, string whatHappened) =>
        orderNum is null
            ? SupplierBookingResult.Refused(
                $"SkyLink /order {whatHappened}: ticketing was never requested, so no ticket was issued; an unpaid order may exist for buyerOrder {bookingId}.")
            : SupplierBookingResult.Unknown(
                $"SkyLink /ticketing for order {orderNum} {whatHappened}: a ticket may have been issued; check with SkyLink.",
                orderNum);

    private static TData GetDataOrThrow<TData>(SkyLinkResponse<TData> response, string path) =>
        response.ErrorCode == SuccessCode
            ? response.Data!
            : throw new InvalidOperationException($"SkyLink {path} returned error {response.ErrorCode} {response.ErrorMsg}.");

    private async Task<SkyLinkResponse<TData>> PostAsync<TBody, TData>(string path, TBody body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadAsync<TData>(response, path, cancellationToken);
    }

    private static async Task<SkyLinkResponse<TData>> ReadAsync<TData>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();

        var skyLinkResponse = await response.Content.ReadFromJsonAsync<SkyLinkResponse<TData>>(JsonOptions, cancellationToken);
        if (skyLinkResponse?.ErrorCode is null || (skyLinkResponse.ErrorCode == SuccessCode && skyLinkResponse.Data is null))
        {
            throw new JsonException($"SkyLink {path} returned an unreadable response.");
        }

        return skyLinkResponse;
    }
}
