using System.ComponentModel.DataAnnotations;

namespace BookingApi.Domain;

public class Wallet
{
    [Key]
    [MaxLength(ColumnLengths.UserId)]
    public required string UserId { get; set; }

    public long BalanceIqd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
