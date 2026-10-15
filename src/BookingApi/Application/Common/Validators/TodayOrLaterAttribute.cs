using System.ComponentModel.DataAnnotations;

namespace BookingApi.Application.Common.Validators;

public class TodayOrLaterAttribute : ValidationAttribute
{
    public TodayOrLaterAttribute()
        : base("The {0} field must be today or later.")
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
            throw new InvalidOperationException($"{nameof(TodayOrLaterAttribute)} can only be used on a DateOnly property.");
        }

        return date >= DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
