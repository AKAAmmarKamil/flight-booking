using BookingApi.Application.Search;
using BookingApi.Application.Suppliers;

namespace BookingApi.Tests.Search;

public class OfferDeduplicatorTests
{
    private static PricedOffer Offer(string supplier, string flightNumber, DateTime departure, long priceIqd) =>
        new(new SupplierOffer(
                Supplier: supplier,
                SupplierReference: $"{supplier}-{flightNumber}",
                Airline: flightNumber[..2],
                FlightNumber: flightNumber,
                From: "BGW",
                To: "IST",
                Departure: departure,
                Arrival: departure.AddMinutes(150),
                Stops: 0,
                OriginalPrice: 0m,
                OriginalCurrency: "USD"),
            priceIqd);

    private static readonly DateTime Tk785Departure = new(2026, 11, 20, 8, 30, 0);

    [Fact]
    public void Deduplication_returns_TK785_from_both_suppliers_once_at_lower_price()
    {
        var aeroHub = Offer("AeroHub", "TK785", Tk785Departure, 455_000);
        var skyLink = Offer("SkyLink", "TK785", Tk785Departure, 446_000);

        var result = OfferDeduplicator.DeduplicateAndSort([aeroHub, skyLink]);

        var single = Assert.Single(result);
        Assert.Equal("SkyLink", single.Offer.Supplier);
        Assert.Equal(446_000, single.PriceIqd);
    }

    [Fact]
    public void Deduplication_matches_departure_to_the_minute_ignoring_seconds()
    {
        var aeroHub = Offer("AeroHub", "TK785", Tk785Departure, 455_000);
        var skyLink = Offer("SkyLink", "TK785", Tk785Departure.AddSeconds(45), 446_000);

        var result = OfferDeduplicator.DeduplicateAndSort([aeroHub, skyLink]);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplication_keeps_same_flight_number_at_different_departure_minute()
    {
        var early = Offer("AeroHub", "TK785", Tk785Departure, 455_000);
        var later = Offer("SkyLink", "TK785", Tk785Departure.AddMinutes(1), 446_000);

        var result = OfferDeduplicator.DeduplicateAndSort([early, later]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplication_sorts_by_price_ascending()
    {
        var expensive = Offer("AeroHub", "TK785", Tk785Departure, 600_000);
        var cheap = Offer("SkyLink", "PC123", Tk785Departure, 300_000);
        var middle = Offer("AeroHub", "FZ210", Tk785Departure, 450_000);

        var result = OfferDeduplicator.DeduplicateAndSort([expensive, cheap, middle]);

        Assert.Equal([300_000L, 450_000L, 600_000L], result.Select(offer => offer.PriceIqd));
    }
}
