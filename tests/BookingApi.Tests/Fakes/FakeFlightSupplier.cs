using BookingApi.Application.Suppliers;

namespace BookingApi.Tests.Fakes;

public sealed class FakeFlightSupplier : IFlightSupplier
{
    private readonly TimeSpan _delay;
    private readonly IReadOnlyList<SupplierOffer> _offers;
    private readonly Exception? _failure;

    public FakeFlightSupplier(string name, string offerIdPrefix, TimeSpan delay, IReadOnlyList<SupplierOffer> offers, Exception? failure = null)
    {
        Name = name;
        OfferIdPrefix = offerIdPrefix;
        _delay = delay;
        _offers = offers;
        _failure = failure;
    }

    public string Name { get; }

    public string OfferIdPrefix { get; }

    public async Task<IReadOnlyList<SupplierOffer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);

        if (_failure != null)
        {
            throw _failure;
        }

        return _offers;
    }

    public Task<SupplierBookingResult> BookAsync(SupplierOffer offer, Passenger passenger, string bookingId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SupplierBookingResult> GetStatusAsync(string supplierReference, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
