using BookingApi.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Api.Controllers;

public abstract class BaseController : ControllerBase
{
    protected IActionResult ToActionResult<T>(ServiceResponse<T> response)
    {
        if (response.Error == null)
        {
            return Ok(response.Data);
        }

        var statusCode = response.Error switch
        {
            ServiceError.NotFound => StatusCodes.Status404NotFound,
            ServiceError.InvalidRequest => StatusCodes.Status400BadRequest,
            ServiceError.InsufficientBalance => StatusCodes.Status402PaymentRequired,
            ServiceError.Conflict => StatusCodes.Status409Conflict,
            ServiceError.Gone => StatusCodes.Status410Gone,
            _ => throw new ArgumentOutOfRangeException(nameof(response), response.Error, "No status code is mapped for this service error.")
        };
        return Problem(detail: response.ServiceMessage, statusCode: statusCode);
    }
}
