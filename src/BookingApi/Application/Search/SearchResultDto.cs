namespace BookingApi.Application.Search;

public class SearchResultDto
{
    public required List<OfferDto> Offers { get; set; }
    public required List<SupplierSearchResultDto> Suppliers { get; set; }
}