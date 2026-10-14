using System.Globalization;
using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public PersistenceTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Persistence_duplicate_idempotency_key_violates_unique_index()
    {
        var offer = NewOffer();
        var idempotencyKey = UniqueId("key");
        await using (var dbContext = _postgres.CreateDbContext())
        {
            dbContext.Offers.Add(offer);
            dbContext.Bookings.Add(NewBooking(offer, idempotencyKey));
            await dbContext.SaveChangesAsync();
        }

        await using var duplicateContext = _postgres.CreateDbContext();
        duplicateContext.Bookings.Add(NewBooking(offer, idempotencyKey));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task Persistence_wallet_balance_cannot_go_negative()
    {
        var userId = UniqueId("user");
        await using var dbContext = _postgres.CreateDbContext();
        dbContext.Wallets.Add(new Wallet { UserId = userId, BalanceIqd = -1, CreatedAt = Now });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
    }

    [Fact]
    public async Task Persistence_wallet_update_below_zero_is_rejected_and_balance_unchanged()
    {
        var userId = UniqueId("user");
        await using (var dbContext = _postgres.CreateDbContext())
        {
            dbContext.Wallets.Add(new Wallet { UserId = userId, BalanceIqd = 100_000, CreatedAt = Now });
            await dbContext.SaveChangesAsync();
        }

        await using var updateContext = _postgres.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => updateContext.Wallets
            .Where(wallet => wallet.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(wallet => wallet.BalanceIqd, wallet => wallet.BalanceIqd - 200_000)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);

        await using var readContext = _postgres.CreateDbContext();
        var wallet = await readContext.Wallets.SingleAsync(storedWallet => storedWallet.UserId == userId);
        Assert.Equal(100_000, wallet.BalanceIqd);
    }

    [Fact]
    public async Task Persistence_booking_history_round_trips_in_order()
    {
        var offer = NewOffer();
        var booking = NewBooking(offer, UniqueId("key"));
        booking.MoveTo(BookingStatus.Paid, "Wallet debited 455000 IQD", Now.AddSeconds(1));
        await using (var dbContext = _postgres.CreateDbContext())
        {
            dbContext.Offers.Add(offer);
            dbContext.Bookings.Add(booking);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = _postgres.CreateDbContext();
        var loaded = await readContext.Bookings
            .Include(storedBooking => storedBooking.History.OrderBy(change => change.Id))
            .SingleAsync(storedBooking => storedBooking.Id == booking.Id);

        Assert.Equal(BookingStatus.Paid, loaded.Status);
        Assert.Equal(Now.AddSeconds(1), loaded.StatusChangedAt);
        Assert.Collection(
            loaded.History,
            created =>
            {
                Assert.Null(created.FromStatus);
                Assert.Equal(BookingStatus.Created, created.ToStatus);
            },
            paid =>
            {
                Assert.Equal(BookingStatus.Created, paid.FromStatus);
                Assert.Equal(BookingStatus.Paid, paid.ToStatus);
                Assert.Equal("Wallet debited 455000 IQD", paid.Note);
            });
    }

    [Fact]
    public async Task Persistence_offer_round_trips_flight_times_and_price_exactly()
    {
        var offer = NewOffer();
        await using (var dbContext = _postgres.CreateDbContext())
        {
            dbContext.Offers.Add(offer);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = _postgres.CreateDbContext();
        var loaded = await readContext.Offers.SingleAsync(storedOffer => storedOffer.Id == offer.Id);

        Assert.Equal(new DateTime(2026, 11, 20, 8, 30, 0), loaded.Departure);
        Assert.Equal(DateTimeKind.Unspecified, loaded.Departure.Kind);
        Assert.Equal(293.10m, loaded.OriginalPrice);
        Assert.Equal("293.10", loaded.OriginalPrice.ToString(CultureInfo.InvariantCulture));
    }

    private static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static Offer NewOffer() => new()
    {
        Id = UniqueId("AH"),
        Supplier = "AeroHub",
        SupplierReference = "aeh_91c2d4",
        Airline = "TK",
        FlightNumber = "TK785",
        From = "BGW",
        To = "IST",
        Departure = new DateTime(2026, 11, 20, 8, 30, 0),
        Arrival = new DateTime(2026, 11, 20, 11, 0, 0),
        Stops = 0,
        OriginalPrice = 293.10m,
        OriginalCurrency = "USD",
        PriceIqd = 455_000,
        Adults = 1,
        CreatedAt = Now,
        ExpiresAt = Now.AddMinutes(30)
    };

    private static Booking NewBooking(Offer offer, string idempotencyKey)
    {
        var booking = new Booking
        {
            Id = UniqueId("b"),
            IdempotencyKey = idempotencyKey,
            RequestHash = new string('a', 64),
            UserId = UniqueId("user"),
            OfferId = offer.Id,
            Supplier = offer.Supplier,
            PriceIqd = offer.PriceIqd,
            PassengerFirstName = "ALI",
            PassengerLastName = "HASSAN",
            PassengerBirthDate = new DateOnly(1990, 1, 15),
            Status = BookingStatus.Created,
            StatusChangedAt = Now
        };
        booking.History.Add(new BookingStatusHistory
        {
            BookingId = booking.Id,
            FromStatus = null,
            ToStatus = BookingStatus.Created,
            Note = "Booking created",
            At = Now
        });
        return booking;
    }
}
