using System.ComponentModel.DataAnnotations;
using BookingApi.Application.Common.Validators;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingApi.Application.Search;

public class SearchForm
{
    [Required]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "From must be a 3-letter airport code in capitals.")]
    public string From { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "To must be a 3-letter airport code in capitals.")]
    public string To { get; set; } = string.Empty;

    [BindRequired]
    [TodayOrLater]
    public DateOnly Date { get; set; }

    [BindRequired]
    [Range(1, 9, ErrorMessage = "Adults must be between 1 and 9.")]
    public int Adults { get; set; }
}
