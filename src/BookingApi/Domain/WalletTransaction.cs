using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Domain;

public class WalletTransaction : BaseEntity<long>
{
    [MaxLength(ColumnLengths.UserId)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Wallet? Wallet { get; set; }

    public WalletTransactionType Type { get; set; }

    public long AmountIqd { get; set; }

    public string? BookingId { get; set; }

    [ForeignKey(nameof(BookingId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Booking? Booking { get; set; }

    public DateTimeOffset At { get; set; }
}
