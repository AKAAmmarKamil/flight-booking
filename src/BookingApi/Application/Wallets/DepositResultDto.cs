namespace BookingApi.Application.Wallets;

public class DepositResultDto
{
    public required string UserId { get; set; }

    public long BalanceIqd { get; set; }
}
