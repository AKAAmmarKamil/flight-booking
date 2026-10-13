namespace BookingApi.Infrastructure.Suppliers.AeroHub;

internal sealed record AeroHubTokenRequest(string Username, string Password);

internal sealed record AeroHubTokenResponse(string AccessToken, int ExpiresIn);

internal sealed record AeroHubSearchRequest(string Origin, string Destination, DateOnly DepartureDate, int Adults);

internal sealed record AeroHubSearchResponse(IReadOnlyList<AeroHubSearchResult> Results);

internal sealed record AeroHubSearchResult(
    string OfferToken,
    string CarrierCode,
    string FlightNo,
    string DepAirport,
    string ArrAirport,
    DateTime DepTime,
    DateTime ArrTime,
    int Stops,
    decimal TotalUsd);

internal sealed record AeroHubBookRequest(string OfferToken, AeroHubPassenger Passenger);

internal sealed record AeroHubPassenger(string FirstName, string LastName, DateOnly BirthDate);

internal sealed record AeroHubBookResponse(string? Status, string? Pnr, string? TicketNumber, string? Reason);
