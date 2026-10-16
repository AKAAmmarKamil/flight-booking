using System.ComponentModel.DataAnnotations;
using BookingApi.Domain;

namespace BookingApi.Application.Bookings;

public class BookingForm
{
    [Required]
    [MaxLength(ColumnLengths.UserId)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string OfferId { get; set; } = string.Empty;

    [Required]
    public PassengerForm Passenger { get; set; } = null!;
}
