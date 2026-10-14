using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Infrastructure.Persistence;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();

    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    public DbSet<Offer> Offers => Set<Offer>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BookingStatus>().HaveConversion<string>();
        configurationBuilder.Properties<WalletTransactionType>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>().ToTable(table =>
            table.HasCheckConstraint("ck_wallets_balance_iqd_non_negative", "balance_iqd >= 0"));

        modelBuilder.Entity<WalletTransaction>().ToTable(table =>
            table.HasCheckConstraint("ck_wallet_transactions_amount_iqd_positive", "amount_iqd > 0"));
    }
}
