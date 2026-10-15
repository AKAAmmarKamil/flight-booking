using BookingApi.Application.Common;

namespace BookingApi.Application.Wallets;

public interface IWalletService
{
    Task<ServiceResponse<DepositResultDto>> DepositAsync(string userId, DepositForm form, CancellationToken cancellationToken);

    Task<ServiceResponse<WalletDto>> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
