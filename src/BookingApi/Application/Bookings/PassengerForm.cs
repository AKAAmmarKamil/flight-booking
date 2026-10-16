using System.ComponentModel.DataAnnotations;
using BookingApi.Application.Common.Validators;
using BookingApi.Domain;

namespace BookingApi.Application.Bookings;

public class PassengerForm
{
    [Required]
    [MaxLength(ColumnLengths.PassengerName)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(ColumnLengths.PassengerName)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [NotInFuture]
    public DateOnly? BirthDate { get; set; }
}
