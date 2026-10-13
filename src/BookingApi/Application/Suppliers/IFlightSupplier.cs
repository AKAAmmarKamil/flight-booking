namespace BookingApi.Application.Suppliers;

public interface IFlightSupplier
{
    string Name { get; }

    Task<IReadOnlyList<SupplierOffer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken);

    Task<SupplierBookingResult> BookAsync(SupplierOffer offer, Passenger passenger, string bookingId, CancellationToken cancellationToken);

    Task<SupplierBookingResult> GetStatusAsync(string supplierReference, CancellationToken cancellationToken);
}
