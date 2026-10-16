using System.Security.Cryptography;
using System.Text.Json;
using BookingApi.Application.Common;
using BookingApi.Application.Suppliers;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Application.Bookings;

public class BookingService : IBookingService
{
    private const string IdempotencyKeyIndexName = "ix_bookings_idempotency_key";

    private readonly BookingDbContext _dbContext;
    private readonly IEnumerable<IFlightSupplier> _suppliers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        BookingDbContext dbContext,
        IEnumerable<IFlightSupplier> suppliers,
        TimeProvider timeProvider,
        ILogger<BookingService> logger)
    {
        _dbContext = dbContext;
        _suppliers = suppliers;
        _timeProvider = timeProvider;
        _logger = logger;
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

        var supplier = _suppliers.SingleOrDefault(supplier => supplier.Name == offer.Supplier)
            ?? throw new InvalidOperationException($"No supplier named '{offer.Supplier}' is registered for offer '{offer.Id}'.");

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

        var result = await CallSupplierAsync(supplier, offer, booking);

        booking.SupplierReference = result.SupplierReference;
        booking.Pnr = result.Pnr;
        booking.TicketNumber = result.TicketNumber;
        booking.MoveTo(ToBookingStatus(result.Outcome), result.Note, _timeProvider.GetUtcNow());
        // Not the request token: the supplier has already answered, and its answer must be recorded.
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        if (booking.Status == BookingStatus.RequiresReview)
        {
            _logger.LogWarning("Booking {BookingId} requires review: {Note}", booking.Id, result.Note);
        }
        else
        {
            _logger.LogInformation("Booking {BookingId} is {Status}: {Note}", booking.Id, booking.Status, result.Note);
        }

        return new ServiceResponse<BookingResultDto>(ToBookingResultDto(booking));
    }

    private async Task<SupplierBookingResult> CallSupplierAsync(IFlightSupplier supplier, Offer offer, Booking booking)
    {
        var passenger = new Passenger(booking.PassengerFirstName, booking.PassengerLastName, booking.PassengerBirthDate);
        try
        {
            // Not the request token: a client disconnect must not abort a paid booking; the adapter's own book timeout ends the call.
            return await supplier.BookAsync(ToSupplierOffer(offer), passenger, booking.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{Supplier} booking call for {BookingId} failed unexpectedly.", supplier.Name, booking.Id);
            return SupplierBookingResult.Unknown(
                $"{supplier.Name} call failed unexpectedly ({exception.GetType().Name}): a ticket may have been issued; check with {supplier.Name}.");
        }
    }

    private static BookingStatus ToBookingStatus(SupplierBookingOutcome outcome) =>
        outcome switch
        {
            SupplierBookingOutcome.Confirmed => BookingStatus.Confirmed,
            SupplierBookingOutcome.Pending => BookingStatus.Pending,
            SupplierBookingOutcome.Refused or SupplierBookingOutcome.Unknown => BookingStatus.RequiresReview,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "No booking status is mapped for this supplier outcome.")
        };

    private static SupplierOffer ToSupplierOffer(Offer offer) =>
        new(
            Supplier: offer.Supplier,
            SupplierReference: offer.SupplierReference,
            Airline: offer.Airline,
            FlightNumber: offer.FlightNumber,
            From: offer.From,
            To: offer.To,
            Departure: offer.Departure,
            Arrival: offer.Arrival,
            Stops: offer.Stops,
            OriginalPrice: offer.OriginalPrice,
            OriginalCurrency: offer.OriginalCurrency);

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
