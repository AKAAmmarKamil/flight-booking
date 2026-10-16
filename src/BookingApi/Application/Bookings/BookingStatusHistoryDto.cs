using BookingApi.Domain;

namespace BookingApi.Application.Bookings;

public class BookingStatusHistoryDto
{
    public DateTimeOffset At { get; set; }

    public BookingStatus? From { get; set; }

    public BookingStatus To { get; set; }

    public required string Note { get; set; }
}
