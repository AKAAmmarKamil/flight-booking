using System.Security.Cryptography;

namespace AeroHubMock;

public static class FakeData
{
    private static readonly string[] Carriers = ["TK", "RJ", "QR", "EK", "FZ", "IA"];

    public static IReadOnlyList<SearchResult> Offers(SearchRequest request)
    {
        var day = request.DepartureDate.ToDateTime(TimeOnly.MinValue);

        var offers = new List<SearchResult>
        {
            new(OfferToken(), "TK", "785", request.Origin, request.Destination,
                day.AddHours(8).AddMinutes(30), day.AddHours(11), 0, 293.10m)
        };

        var extra = Random.Shared.Next(2, 5);
        for (var i = 0; i < extra; i++)
        {
            var departure = day.AddMinutes(Random.Shared.Next(0, 96) * 15);
            var stops = Random.Shared.Next(0, 2);
            var duration = TimeSpan.FromMinutes(Random.Shared.Next(120, 300) + stops * 120);

            offers.Add(new(
                OfferToken(),
                Carriers[Random.Shared.Next(Carriers.Length)],
                Random.Shared.Next(100, 1000).ToString(),
                request.Origin,
                request.Destination,
                departure,
                departure + duration,
                stops,
                Random.Shared.Next(15000, 60000) / 100m));
        }

        return offers;
    }

    public static string Pnr() => RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6);

    public static string TicketNumber() => "235" + RandomNumberGenerator.GetString("0123456789", 10);

    private static string OfferToken() => "aeh_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
}
