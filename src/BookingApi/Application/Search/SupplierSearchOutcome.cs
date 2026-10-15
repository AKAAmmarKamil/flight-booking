namespace BookingApi.Application.Search;

public sealed record SupplierSearchOutcome(string SupplierName, SupplierSearchStatus Status, IReadOnlyList<PricedOffer> Offers);
