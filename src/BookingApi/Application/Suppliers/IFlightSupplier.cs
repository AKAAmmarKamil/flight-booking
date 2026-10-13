namespace BookingApi.Application.Suppliers;

public interface IFlightSupplier
{
    string Name { get; }

    Task<IReadOnlyList<SupplierOffer>> SearchAsync(SearchRequest request, CancellationToken ct);

    Task<SupplierBookingResult> BookAsync(SupplierOffer offer, Passenger passenger, string bookingId, CancellationToken ct);

    Task<SupplierBookingResult> GetStatusAsync(string supplierReference, CancellationToken ct);
}
