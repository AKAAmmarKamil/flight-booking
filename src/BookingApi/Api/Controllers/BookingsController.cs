using System.ComponentModel.DataAnnotations;
using BookingApi.Application.Bookings;
using BookingApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Api.Controllers;

[ApiController]
[Route("bookings")]
public class BookingsController : BaseController
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BookingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> Book(
        [FromHeader(Name = "Idempotency-Key")][Required][MaxLength(ColumnLengths.IdempotencyKey)] string idempotencyKey,
        BookingForm form,
        CancellationToken cancellationToken)
    {
        var response = await _bookingService.BookAsync(idempotencyKey, form, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("{bookingId}")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string bookingId, CancellationToken cancellationToken)
    {
        var response = await _bookingService.GetByIdAsync(bookingId, cancellationToken);
        return ToActionResult(response);
    }
}
