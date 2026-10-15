using BookingApi.Application.Common;
namespace BookingApi.Application.Search;

public interface ISearchService
{
    Task<ServiceResponse<SearchResultDto>> SearchAsync(SearchForm form, CancellationToken cancellationToken);
}
