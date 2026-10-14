using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Domain;

[Index(nameof(IdempotencyKey), IsUnique = true)]
public class Booking : BaseEntity<string>
{
    [MaxLength(ColumnLengths.IdempotencyKey)]
    public required string IdempotencyKey { get; set; }

    [MaxLength(ColumnLengths.RequestHash)]
    public required string RequestHash { get; set; }

    [MaxLength(ColumnLengths.UserId)]
    public required string UserId { get; set; }

    public required string OfferId { get; set; }

    [ForeignKey(nameof(OfferId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Offer? Offer { get; set; }

    [MaxLength(ColumnLengths.SupplierName)]
    public required string Supplier { get; set; }

    public long PriceIqd { get; set; }

    [MaxLength(ColumnLengths.PassengerName)]
    public required string PassengerFirstName { get; set; }

    [MaxLength(ColumnLengths.PassengerName)]
    public required string PassengerLastName { get; set; }

    public DateOnly PassengerBirthDate { get; set; }

    public BookingStatus Status { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    public string? SupplierReference { get; set; }

    public string? Pnr { get; set; }

    public string? TicketNumber { get; set; }

    public List<BookingStatusHistory> History { get; set; } = [];

    public void MoveTo(BookingStatus newStatus, string note, DateTimeOffset at)
    {
        History.Add(new BookingStatusHistory
        {
            BookingId = Id,
            FromStatus = Status,
            ToStatus = newStatus,
            Note = note,
            At = at
        });
        Status = newStatus;
        StatusChangedAt = at;
    }
}
