using BookingApi.Application.Common;

namespace BookingApi.Application.Bookings;

public interface IBookingService
{
    Task<ServiceResponse<BookingResultDto>> BookAsync(string idempotencyKey, BookingForm form, CancellationToken cancellationToken);
}
