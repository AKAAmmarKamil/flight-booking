namespace BookingApi.Application.Suppliers;

public sealed record SupplierOffer(
    string Supplier,
    string SupplierReference,
    string Airline,
    string FlightNumber,
    string From,
    string To,
    DateTime Departure,
    DateTime Arrival,
    int Stops,
    decimal OriginalPrice,
    string OriginalCurrency);
