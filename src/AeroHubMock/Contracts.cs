namespace AeroHubMock;

public sealed record TokenRequest(string Username, string Password);

public sealed record TokenResponse(string AccessToken, int ExpiresIn);

public sealed record SearchRequest(string Origin, string Destination, DateOnly DepartureDate, int Adults);

public sealed record SearchResponse(IReadOnlyList<SearchResult> Results);

public sealed record SearchResult(
    string OfferToken,
    string CarrierCode,
    string FlightNo,
    string DepAirport,
    string ArrAirport,
    DateTime DepTime,
    DateTime ArrTime,
    int Stops,
    decimal TotalUsd);

public sealed record BookRequest(string OfferToken, BookPassenger Passenger);

public sealed record BookPassenger(string FirstName, string LastName, DateOnly BirthDate);

public sealed record BookResponse(string Status, string? Pnr = null, string? TicketNumber = null, string? Reason = null);
