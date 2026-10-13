using BookingApi.Application.Suppliers;

namespace BookingApi.Application.Search;

public sealed record PricedOffer(SupplierOffer Offer, long PriceIqd);
