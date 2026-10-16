namespace BookingApi.Application.Bookings;

public class PendingPollerOptions
{
    public int IntervalSeconds { get; set; }

    public int PendingTimeoutSeconds { get; set; }

    public int StatusTimeoutSeconds { get; set; }
}
