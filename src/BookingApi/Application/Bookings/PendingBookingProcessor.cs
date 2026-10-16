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

            string note;
            if (result?.Outcome == SupplierBookingOutcome.Confirmed)
            {
                note = result.Note;
                booking.Pnr = result.Pnr;
                booking.TicketNumber = result.TicketNumber;
                booking.MoveTo(BookingStatus.Confirmed, note, now);
            }
            else if (result?.Outcome is SupplierBookingOutcome.Refused or SupplierBookingOutcome.Unknown)
            {
                note = result.Note;
                booking.MoveTo(BookingStatus.RequiresReview, note, now);
            }
            else if (now - booking.StatusChangedAt >= TimeSpan.FromSeconds(_options.PendingTimeoutSeconds))
            {
                note = $"{booking.Supplier} order {booking.SupplierReference} still not issued {_options.PendingTimeoutSeconds} s after it became Pending: "
                       + $"a ticket may still be issued; check with {booking.Supplier}.";
                booking.MoveTo(BookingStatus.RequiresReview, note, now);
            }
            else
            {
                continue;
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance moved this booking out of Pending first; its change stands, ours is dropped.
                _logger.LogInformation("Booking {BookingId} was already moved out of Pending by another instance.", booking.Id);
                DiscardUnsavedChanges();
                continue;
            }

            if (booking.Status == BookingStatus.RequiresReview)
            {
                _logger.LogWarning("Booking {BookingId} requires review: {Note}", booking.Id, note);
            }
            else
            {
                _logger.LogInformation("Booking {BookingId} is {Status}: {Note}", booking.Id, booking.Status, note);
            }
        }
    }

    private void DiscardUnsavedChanges()
    {
        // Only the rejected booking and its new history row: the bookings still waiting in this run stay tracked.
        var unsavedEntries = _dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State != EntityState.Unchanged)
            .ToList();
        foreach (var entry in unsavedEntries)
        {
            entry.State = EntityState.Detached;
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
