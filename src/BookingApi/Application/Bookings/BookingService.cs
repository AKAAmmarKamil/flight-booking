using System.Security.Cryptography;
using System.Text.Json;
using BookingApi.Application.Common;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Application.Bookings;

public class BookingService : IBookingService
{
    private const string IdempotencyKeyIndexName = "ix_bookings_idempotency_key";

    private readonly BookingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public BookingService(BookingDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<ServiceResponse<BookingResultDto>> BookAsync(string idempotencyKey, BookingForm form, CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(form);

        var existingBooking = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existingBooking != null)
        {
            return ToRepeatedRequestResponse(existingBooking, requestHash);
        }

        var offer = await _dbContext.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(offer => offer.Id == form.OfferId, cancellationToken);
        if (offer == null)
        {
            return new ServiceResponse<BookingResultDto>(ServiceError.NotFound, $"Offer '{form.OfferId}' does not exist.");
        }

        var now = _timeProvider.GetUtcNow();
        if (offer.ExpiresAt <= now)
        {
            return new ServiceResponse<BookingResultDto>(ServiceError.Gone, $"Offer '{offer.Id}' expired at {offer.ExpiresAt:O}. Search again for a new offer.");
        }

        if (offer.Adults > 1)
        {
            return new ServiceResponse<BookingResultDto>(ServiceError.InvalidRequest,
                $"Offer '{offer.Id}' was priced for {offer.Adults} adults, but a booking takes one passenger. Search again with adults=1.");
        }

        var priceIqd = offer.PriceIqd;
        var booking = new Booking
        {
            Id = $"b-{Guid.NewGuid():N}",
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            UserId = form.UserId,
            OfferId = offer.Id,
            Supplier = offer.Supplier,
            PriceIqd = priceIqd,
            PassengerFirstName = form.Passenger.FirstName,
            PassengerLastName = form.Passenger.LastName,
            PassengerBirthDate = form.Passenger.BirthDate!.Value,
            Status = BookingStatus.Created,
            StatusChangedAt = now
        };
        booking.History.Add(new BookingStatusHistory
        {
            BookingId = booking.Id,
            FromStatus = null,
            ToStatus = BookingStatus.Created,
            Note = $"Booking created for offer {offer.Id} ({offer.FlightNumber} {offer.From}-{offer.To}, {offer.Supplier}).",
            At = now
        });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Bookings.Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                   {
                                                       SqlState: PostgresErrorCodes.UniqueViolation,
                                                       ConstraintName: IdempotencyKeyIndexName
                                                   })
        {
            // A request with the same key was inserting at the same moment: PostgreSQL held our INSERT until that one committed.
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();

            var concurrentBooking = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException($"Idempotency key conflict, but no booking holds key '{idempotencyKey}'.");
            return ToRepeatedRequestResponse(concurrentBooking, requestHash);
        }

        var debitedRows = await _dbContext.Wallets
            .Where(wallet => wallet.UserId == form.UserId && wallet.BalanceIqd >= priceIqd)
            .ExecuteUpdateAsync(setters => setters.SetProperty(wallet => wallet.BalanceIqd, wallet => wallet.BalanceIqd - priceIqd), cancellationToken);
        if (debitedRows == 0)
        {
            // Returning without a commit rolls the transaction back: the booking row is gone and the key is free again.
            return new ServiceResponse<BookingResultDto>(ServiceError.InsufficientBalance,
                $"The wallet of user '{form.UserId}' has less than {priceIqd} IQD.");
        }

        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            UserId = form.UserId,
            Type = WalletTransactionType.Debit,
            AmountIqd = priceIqd,
            BookingId = booking.Id,
            At = now
        });
        booking.MoveTo(BookingStatus.Paid, $"Wallet debited {priceIqd} IQD", now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Not the request token: a commit cancelled half-way may still succeed on the server, leaving us unaware the money was taken.
        await transaction.CommitAsync(CancellationToken.None);

        return new ServiceResponse<BookingResultDto>(ToBookingResultDto(booking));
    }

    private Task<Booking?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        _dbContext.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(booking => booking.IdempotencyKey == idempotencyKey, cancellationToken);

    private static ServiceResponse<BookingResultDto> ToRepeatedRequestResponse(Booking existingBooking, string requestHash)
    {
        if (existingBooking.RequestHash != requestHash)
        {
            return new ServiceResponse<BookingResultDto>(ServiceError.Conflict,
                "This Idempotency-Key was already used with a different request body.");
        }

        return new ServiceResponse<BookingResultDto>(ToBookingResultDto(existingBooking));
    }

    private static string ComputeRequestHash(BookingForm form) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(form)));

    private static BookingResultDto ToBookingResultDto(Booking booking) =>
        new()
        {
            BookingId = booking.Id,
            Status = booking.Status,
            PriceIqd = booking.PriceIqd,
            Pnr = booking.Pnr,
            TicketNumber = booking.TicketNumber
        };
}
