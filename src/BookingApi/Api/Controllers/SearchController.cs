using BookingApi.Application.Search;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Api.Controllers;

[ApiController]
[Route("search")]
public class SearchController : BaseController
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SearchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] SearchForm form, CancellationToken cancellationToken)
    {
        var response = await _searchService.SearchAsync(form, cancellationToken);
        return ToActionResult(response);
    }
}
