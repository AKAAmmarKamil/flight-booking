namespace BookingApi.Application.Suppliers;

public enum SupplierBookingOutcome
{
    Confirmed,
    Pending,
    Refused,
    Unknown
}

public sealed record SupplierBookingResult(
    SupplierBookingOutcome Outcome,
    string Note,
    string? SupplierReference = null,
    string? Pnr = null,
    string? TicketNumber = null)
{
    public static SupplierBookingResult Confirmed(string? supplierReference, string pnr, string ticketNumber, string note) =>
        new(SupplierBookingOutcome.Confirmed, note, supplierReference, pnr, ticketNumber);

    public static SupplierBookingResult Pending(string supplierReference, string note) =>
        new(SupplierBookingOutcome.Pending, note, supplierReference);

    public static SupplierBookingResult Refused(string note, string? supplierReference = null) =>
        new(SupplierBookingOutcome.Refused, note, supplierReference);

    public static SupplierBookingResult Unknown(string note, string? supplierReference = null) =>
        new(SupplierBookingOutcome.Unknown, note, supplierReference);
}
