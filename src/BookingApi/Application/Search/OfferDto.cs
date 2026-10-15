namespace BookingApi.Application.Search;

public class OfferDto
{
    public required string OfferId { get; set; }

    public required string Supplier { get; set; }
    
    public required string Airline { get; set; }
    
    public required string FlightNumber { get; set; }
    
    public required string From { get; set; }
    
    public required string To { get; set; }
    
    public DateTime Departure { get; set; }
    
    public DateTime Arrival { get; set; }
    
    public int Stops { get; set; }
    
    public decimal OriginalPrice { get; set; }
    
    public long PriceIqd { get; set; }
    
    public required string OriginalCurrency { get; set; }
}