using BookingApi.Domain;

namespace BookingApi.Application.Bookings;

public class BookingDto
{
    public required string BookingId { get; set; }

    public BookingStatus Status { get; set; }

    public long PriceIqd { get; set; }

    public required string Supplier { get; set; }

    public string? Pnr { get; set; }

    public string? TicketNumber { get; set; }

    public required List<BookingStatusHistoryDto> History { get; set; }
}
