using System.ComponentModel.DataAnnotations;

namespace BookingApi.Application.Common.Validators;

public class NotInFutureAttribute : ValidationAttribute
{
    public NotInFutureAttribute()
        : base("The {0} field must not be in the future.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value == null)
        {
            return true;
        }

        if (value is not DateOnly date)
        {
            throw new InvalidOperationException($"{nameof(NotInFutureAttribute)} can only be used on a DateOnly property.");
        }

        return date <= DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
