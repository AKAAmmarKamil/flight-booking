using BookingApi.Application.Suppliers;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingApi.Application.Bookings;

public class PendingBookingProcessor
{
    private readonly BookingDbContext _dbContext;
    private readonly IEnumerable<IFlightSupplier> _suppliers;
    private readonly TimeProvider _timeProvider;
    private readonly PendingPollerOptions _options;
    private readonly ILogger<PendingBookingProcessor> _logger;

    public PendingBookingProcessor(
        BookingDbContext dbContext,
        IEnumerable<IFlightSupplier> suppliers,
        TimeProvider timeProvider,
        IOptions<PendingPollerOptions> options,
        ILogger<PendingBookingProcessor> logger)
    {
        _dbContext = dbContext;
        _suppliers = suppliers;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var pendingBookings = await _dbContext.Bookings
            .Where(booking => booking.Status == BookingStatus.Pending)
            .OrderBy(booking => booking.StatusChangedAt)
            .ToListAsync(cancellationToken);

        foreach (var booking in pendingBookings)
        {
            var result = await GetSupplierStatusAsync(booking, cancellationToken);
            var now = _timeProvider.GetUtcNow();

            if (result?.Outcome == SupplierBookingOutcome.Confirmed)
            {
                booking.Pnr = result.Pnr;
                booking.TicketNumber = result.TicketNumber;
                booking.MoveTo(BookingStatus.Confirmed, result.Note, now);
                _logger.LogInformation("Booking {BookingId} is Confirmed: {Note}", booking.Id, result.Note);
            }
            else if (result?.Outcome is SupplierBookingOutcome.Refused or SupplierBookingOutcome.Unknown)
            {
                booking.MoveTo(BookingStatus.RequiresReview, result.Note, now);
                _logger.LogWarning("Booking {BookingId} requires review: {Note}", booking.Id, result.Note);
            }
            else if (now - booking.StatusChangedAt >= TimeSpan.FromSeconds(_options.PendingTimeoutSeconds))
            {
                var note = $"{booking.Supplier} order {booking.SupplierReference} still not issued {_options.PendingTimeoutSeconds} s after it became Pending: "
                           + $"a ticket may still be issued; check with {booking.Supplier}.";
                booking.MoveTo(BookingStatus.RequiresReview, note, now);
                _logger.LogWarning("Booking {BookingId} requires review: {Note}", booking.Id, note);
            }
            else
            {
                continue;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<SupplierBookingResult?> GetSupplierStatusAsync(Booking booking, CancellationToken cancellationToken)
    {
        using var statusTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        statusTimeout.CancelAfter(TimeSpan.FromSeconds(_options.StatusTimeoutSeconds));
        try
        {
            var supplier = _suppliers.SingleOrDefault(supplier => supplier.Name == booking.Supplier)
                ?? throw new InvalidOperationException($"No supplier named '{booking.Supplier}' is registered.");
            var supplierReference = booking.SupplierReference
                ?? throw new InvalidOperationException("The booking is Pending but has no supplier reference.");

            return await supplier.GetStatusAsync(supplierReference, statusTimeout.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The booking stays Pending and is checked again on the next run; the pending timeout still ends it.
            _logger.LogWarning(exception, "{Supplier} status check for booking {BookingId} failed.", booking.Supplier, booking.Id);
            return null;
        }
    }
}
