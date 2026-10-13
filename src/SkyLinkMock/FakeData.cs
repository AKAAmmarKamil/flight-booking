using System.Security.Cryptography;

namespace SkyLinkMock;

public static class FakeData
{
    private static readonly string[] Airlines = ["TK", "RJ", "QR", "EK", "FZ", "IA"];
    private static readonly string[] Hubs = ["AMM", "DOH", "DXB", "CAI", "BEY"];

    public static ShoppingData Shopping(ShoppingSearch search)
    {
        var day = search.Date.ToDateTime(TimeOnly.MinValue);
        var builder = new ShoppingBuilder();

        builder.Add(265.40m, new Leg("TK", "785", search.From, search.To, day.AddHours(8).AddMinutes(30), day.AddHours(11)));

        var hubs = Hubs.Where(hub => hub != search.From && hub != search.To).ToArray();
        var hub = hubs[Random.Shared.Next(hubs.Length)];
        var airline = Airlines[Random.Shared.Next(Airlines.Length)];
        var firstDeparture = day.AddMinutes(Random.Shared.Next(0, 64) * 15);
        var firstArrival = firstDeparture.AddMinutes(Random.Shared.Next(60, 150));
        var secondDeparture = firstArrival.AddMinutes(Random.Shared.Next(60, 180));
        var secondArrival = secondDeparture.AddMinutes(Random.Shared.Next(90, 210));
        builder.Add(
            Random.Shared.Next(18000, 45000) / 100m,
            new Leg(airline, FlightNum(), search.From, hub, firstDeparture, firstArrival),
            new Leg(airline, FlightNum(), hub, search.To, secondDeparture, secondArrival));

        var extra = Random.Shared.Next(1, 4);
        for (var i = 0; i < extra; i++)
        {
            var departure = day.AddMinutes(Random.Shared.Next(0, 96) * 15);
            builder.Add(
                Random.Shared.Next(15000, 55000) / 100m,
                new Leg(
                    Airlines[Random.Shared.Next(Airlines.Length)],
                    FlightNum(),
                    search.From,
                    search.To,
                    departure,
                    departure.AddMinutes(Random.Shared.Next(120, 300))));
        }

        return builder.Build();
    }

    public static string Pnr() => RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6);

    public static string TicketNumber() => "607" + RandomNumberGenerator.GetString("0123456789", 10);

    private static string FlightNum() => Random.Shared.Next(100, 1000).ToString();

    private sealed record Leg(string Airline, string FlightNum, string Dep, string Arr, DateTime DepartsAt, DateTime ArrivesAt);

    private sealed class ShoppingBuilder
    {
        private readonly List<Solution> _solutions = [];
        private readonly List<Flight> _flights = [];
        private readonly List<Segment> _segments = [];

        public void Add(decimal priceEur, params Leg[] legs)
        {
            var segmentIds = new List<string>();
            foreach (var leg in legs)
            {
                var segmentId = $"G{_segments.Count + 1}";
                _segments.Add(new Segment(
                    segmentId,
                    leg.Airline,
                    leg.FlightNum,
                    leg.Dep,
                    leg.Arr,
                    DateOnly.FromDateTime(leg.DepartsAt),
                    leg.DepartsAt.ToString("HH:mm"),
                    DateOnly.FromDateTime(leg.ArrivesAt),
                    leg.ArrivesAt.ToString("HH:mm")));
                segmentIds.Add(segmentId);
            }

            var flightId = $"F{_flights.Count + 1}";
            _flights.Add(new Flight(flightId, segmentIds));
            _solutions.Add(new Solution(SolutionId(), [flightId], priceEur));
        }

        public ShoppingData Build() => new(_solutions, _flights, _segments);

        private static string SolutionId() => "S" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    }
}
