using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookingApi.Application.Suppliers;
using Microsoft.Extensions.Options;

namespace BookingApi.Infrastructure.Suppliers.AeroHub;

public sealed class AeroHubSupplier : IFlightSupplier
{
    public const string SupplierName = "AeroHub";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly AeroHubTokenCache _tokenCache;
    private readonly AeroHubOptions _options;

    public AeroHubSupplier(HttpClient httpClient, AeroHubTokenCache tokenCache, IOptions<AeroHubOptions> options)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _options = options.Value;
    }

    public string Name => SupplierName;

    public async Task<IReadOnlyList<SupplierOffer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var body = new AeroHubSearchRequest(request.From, request.To, request.Date, request.Adults);

        using var response = await PostWithTokenAsync("/search", body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var searchResponse = await response.Content.ReadFromJsonAsync<AeroHubSearchResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("AeroHub returned an empty search response.");

        return searchResponse.Results.Select(ToSupplierOffer).ToList();
    }

    public async Task<SupplierBookingResult> BookAsync(SupplierOffer offer, Passenger passenger, string bookingId, CancellationToken cancellationToken)
    {
        var body = new AeroHubBookRequest(
            offer.SupplierReference,
            new AeroHubPassenger(passenger.FirstName, passenger.LastName, passenger.BirthDate));

        using var bookTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bookTimeout.CancelAfter(TimeSpan.FromSeconds(_options.BookTimeoutSeconds));

        try
        {
            using var response = await PostWithTokenAsync("/book", body, bookTimeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return SupplierBookingResult.Unknown(
                    $"AeroHub answered HTTP {(int)response.StatusCode}: a ticket may have been issued; check with AeroHub.");
            }

            var bookResponse = await response.Content.ReadFromJsonAsync<AeroHubBookResponse>(JsonOptions, bookTimeout.Token);
            return ToBookingResult(bookResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SupplierBookingResult.Unknown(
                $"AeroHub did not answer within {_options.BookTimeoutSeconds} s: a ticket may have been issued; check with AeroHub.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return SupplierBookingResult.Unknown(
                $"AeroHub call failed ({exception.GetType().Name}): a ticket may have been issued; check with AeroHub.");
        }
    }

    public Task<SupplierBookingResult> GetStatusAsync(string supplierReference, CancellationToken cancellationToken) =>
        throw new NotSupportedException("AeroHub has no booking lookup, and AeroHub bookings never become Pending.");

    private static SupplierOffer ToSupplierOffer(AeroHubSearchResult result) =>
        new(
            Supplier: SupplierName,
            SupplierReference: result.OfferToken,
            Airline: result.CarrierCode,
            FlightNumber: result.CarrierCode + result.FlightNo,
            From: result.DepAirport,
            To: result.ArrAirport,
            Departure: result.DepTime,
            Arrival: result.ArrTime,
            Stops: result.Stops,
            OriginalPrice: result.TotalUsd,
            OriginalCurrency: "USD");

    private static SupplierBookingResult ToBookingResult(AeroHubBookResponse? bookResponse) =>
        bookResponse switch
        {
            { Status: "CONFIRMED", Pnr: { } pnr, TicketNumber: { } ticketNumber } =>
                SupplierBookingResult.Confirmed(pnr, pnr, ticketNumber, "Confirmed by AeroHub."),
            { Status: "FAILED" } =>
                SupplierBookingResult.Refused($"AeroHub refused the booking ({bookResponse.Reason ?? "no reason given"}): no ticket was issued."),
            _ =>
                SupplierBookingResult.Unknown("AeroHub answered with an unreadable booking status: a ticket may have been issued; check with AeroHub.")
        };

    private async Task<HttpResponseMessage> PostWithTokenAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
    {
        var token = await _tokenCache.GetTokenAsync(FetchTokenAsync, cancellationToken);
        var response = await PostAsync(path, body, token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _tokenCache.Invalidate(token);
        var newToken = await _tokenCache.GetTokenAsync(FetchTokenAsync, cancellationToken);
        return await PostAsync(path, body, newToken, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<AeroHubTokenResponse> FetchTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/token", new AeroHubTokenRequest(_options.Username, _options.Password), JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AeroHubTokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("AeroHub returned an empty token response.");
    }
}
