using BookingApi.Domain;

namespace BookingApi.Application.Wallets;

public class WalletTransactionDto
{
    public WalletTransactionType Type { get; set; }

    public long AmountIqd { get; set; }

    public DateTimeOffset At { get; set; }
}
