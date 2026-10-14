namespace BookingApi.Domain;

public class BookingStatusHistory : BaseEntity<long>
{
    public required string BookingId { get; set; }

    public BookingStatus? FromStatus { get; set; }

    public BookingStatus ToStatus { get; set; }

    public required string Note { get; set; }

    public DateTimeOffset At { get; set; }
}
