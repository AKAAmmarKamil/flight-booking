namespace BookingApi.Application.Search;

public static class OfferDeduplicator
{
    public static IReadOnlyList<PricedOffer> DeduplicateAndSort(IEnumerable<PricedOffer> offers) =>
        offers
            .GroupBy(FlightIdentity.Of)
            .Select(sameFlightOffers => sameFlightOffers.MinBy(offer => offer.PriceIqd)!)
            .OrderBy(offer => offer.PriceIqd)
            .ToList();

    private sealed record FlightIdentity(string Airline, string FlightNumber, DateTime DepartureToTheMinute)
    {
        public static FlightIdentity Of(PricedOffer priced)
        {
            var departure = priced.Offer.Departure;
            var departureToTheMinute = new DateTime(departure.Year, departure.Month, departure.Day, departure.Hour, departure.Minute, 0);

            return new FlightIdentity(priced.Offer.Airline, priced.Offer.FlightNumber, departureToTheMinute);
        }
    }
}
