using BookingApi.Application.Common;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Application.Wallets;

public class WalletService : IWalletService
{
    private readonly BookingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public WalletService(BookingDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<ServiceResponse<DepositResultDto>> DepositAsync(string userId, DepositForm form, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updatedRows = await IncreaseBalanceAsync(userId, form.AmountIqd, cancellationToken);
        if (updatedRows == 0)
        {
            var newWallet = new Wallet { UserId = userId, BalanceIqd = form.AmountIqd, CreatedAt = now };
            _dbContext.Wallets.Add(newWallet);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent first deposit created the wallet. EF rolled back to its automatic savepoint, so the transaction is still usable.
                _dbContext.Entry(newWallet).State = EntityState.Detached;
                await IncreaseBalanceAsync(userId, form.AmountIqd, cancellationToken);
            }
        }

        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            UserId = userId,
            Type = WalletTransactionType.Deposit,
            AmountIqd = form.AmountIqd,
            At = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var balanceIqd = await _dbContext.Wallets
            .Where(wallet => wallet.UserId == userId)
            .Select(wallet => wallet.BalanceIqd)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ServiceResponse<DepositResultDto>(new DepositResultDto
        {
            UserId = userId,
            BalanceIqd = balanceIqd
        });
    }

    public async Task<ServiceResponse<WalletDto>> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        var wallet = await _dbContext.Wallets
            .Where(wallet => wallet.UserId == userId)
            .Select(wallet => new WalletDto
            {
                UserId = wallet.UserId,
                BalanceIqd = wallet.BalanceIqd,
                Transactions = _dbContext.WalletTransactions
                    .Where(walletTransaction => walletTransaction.UserId == wallet.UserId)
                    .OrderBy(walletTransaction => walletTransaction.At)
                    .ThenBy(walletTransaction => walletTransaction.Id)
                    .Select(walletTransaction => new WalletTransactionDto
                    {
                        Type = walletTransaction.Type,
                        AmountIqd = walletTransaction.AmountIqd,
                        At = walletTransaction.At
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (wallet == null)
        {
            return new ServiceResponse<WalletDto>(ServiceError.NotFound, $"No wallet exists for user '{userId}'.");
        }

        return new ServiceResponse<WalletDto>(wallet);
    }

    private Task<int> IncreaseBalanceAsync(string userId, long amountIqd, CancellationToken cancellationToken)
    {
        return _dbContext.Wallets
            .Where(wallet => wallet.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(wallet => wallet.BalanceIqd, wallet => wallet.BalanceIqd + amountIqd), cancellationToken);
    }
}
