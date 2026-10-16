using BookingApi.Domain;

namespace BookingApi.Application.Bookings;

public class BookingResultDto
{
    public required string BookingId { get; set; }

    public BookingStatus Status { get; set; }

    public long PriceIqd { get; set; }

    public string? Pnr { get; set; }

    public string? TicketNumber { get; set; }
}
