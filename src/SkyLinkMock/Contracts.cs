namespace SkyLinkMock;

public sealed record Auth(string PartnerId, string Sign);

public sealed record SkyLinkResponse(string ErrorCode, string? ErrorMsg = null, object? Data = null);

public sealed record ShoppingRequest(Auth? Auth, ShoppingSearch Search);

public sealed record ShoppingSearch(string From, string To, DateOnly Date, int Adt);

public sealed record ShoppingData(IReadOnlyList<Solution> Solutions, IReadOnlyList<Flight> Flights, IReadOnlyList<Segment> Segments);

public sealed record Solution(string SolutionId, IReadOnlyList<string> FlightIds, decimal PriceEur);

public sealed record Flight(string FlightId, IReadOnlyList<string> SegmentIds);

public sealed record Segment(
    string SegmentId,
    string Airline,
    string FlightNum,
    string Dep,
    string Arr,
    DateOnly DepDate,
    string DepTime,
    DateOnly ArrDate,
    string ArrTime);

public sealed record OrderRequest(Auth? Auth, string SolutionId, OrderPassenger Passenger, string BuyerOrder);

public sealed record OrderPassenger(string FirstName, string LastName, DateOnly Birthday);

public sealed record TicketingRequest(Auth? Auth, string OrderNum);

public sealed record OrderData(string OrderNum, string Status, string? Pnr = null, string? TicketNumber = null);
