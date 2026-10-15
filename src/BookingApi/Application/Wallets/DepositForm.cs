using System.ComponentModel.DataAnnotations;

namespace BookingApi.Application.Wallets;

public class DepositForm
{
    // Not Range(int, int): it converts with Convert.ToInt32, which throws (500) above int.MaxValue.
    [Range(typeof(long), "1", "1000000000")]
    public long AmountIqd { get; set; }
}
