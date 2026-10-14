namespace BookingApi.Infrastructure.Suppliers.SkyLink;

internal sealed record SkyLinkAuth(string PartnerId, string Sign);

internal sealed record SkyLinkResponse<TData>(string? ErrorCode, string? ErrorMsg, TData? Data);

internal sealed record SkyLinkShoppingRequest(SkyLinkAuth Auth, SkyLinkSearch Search);

internal sealed record SkyLinkSearch(string From, string To, DateOnly Date, int Adt);

internal sealed record SkyLinkShoppingData(
    IReadOnlyList<SkyLinkSolution> Solutions,
    IReadOnlyList<SkyLinkFlight> Flights,
    IReadOnlyList<SkyLinkSegment> Segments);

internal sealed record SkyLinkSolution(string SolutionId, IReadOnlyList<string> FlightIds, decimal PriceEur);

internal sealed record SkyLinkFlight(string FlightId, IReadOnlyList<string> SegmentIds);

internal sealed record SkyLinkSegment(
    string SegmentId,
    string Airline,
    string FlightNum,
    string Dep,
    string Arr,
    DateOnly DepDate,
    string DepTime,
    DateOnly ArrDate,
    string ArrTime);

internal sealed record SkyLinkOrderRequest(SkyLinkAuth Auth, string SolutionId, SkyLinkPassenger Passenger, string BuyerOrder);

internal sealed record SkyLinkPassenger(string FirstName, string LastName, DateOnly Birthday);

internal sealed record SkyLinkTicketingRequest(SkyLinkAuth Auth, string OrderNum);

internal sealed record SkyLinkOrderData(string OrderNum, string Status, string? Pnr, string? TicketNumber);
