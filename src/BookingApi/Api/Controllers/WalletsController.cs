using System.ComponentModel.DataAnnotations;
using BookingApi.Application.Wallets;
using BookingApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Api.Controllers;

[ApiController]
[Route("wallets")]
public class WalletsController : BaseController
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost("{userId}/deposit")]
    [ProducesResponseType(typeof(DepositResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Deposit([MaxLength(ColumnLengths.UserId)] string userId, DepositForm form, CancellationToken cancellationToken)
    {
        var response = await _walletService.DepositAsync(userId, form, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByUserId([MaxLength(ColumnLengths.UserId)] string userId, CancellationToken cancellationToken)
    {
        var response = await _walletService.GetByUserIdAsync(userId, cancellationToken);
        return ToActionResult(response);
    }
}
