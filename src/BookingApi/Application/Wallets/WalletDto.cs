namespace BookingApi.Application.Wallets;

public class WalletDto
{
    public required string UserId { get; set; }

    public long BalanceIqd { get; set; }

    public required List<WalletTransactionDto> Transactions { get; set; }
}
