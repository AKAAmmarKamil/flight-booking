namespace BookingApi.Application.Search;

public class SupplierSearchResultDto
{
    public required string Name { get; set; }
    public SupplierSearchStatus Status { get; set; }
    public int Count { get; set; }
}