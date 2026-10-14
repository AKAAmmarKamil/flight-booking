using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookingApi.Domain;

public class Offer : BaseEntity<string>
{
    [MaxLength(ColumnLengths.SupplierName)]
    public required string Supplier { get; set; }

    public required string SupplierReference { get; set; }

    public required string Airline { get; set; }

    public required string FlightNumber { get; set; }

    [Column("origin")]
    public required string From { get; set; }

    [Column("destination")]
    public required string To { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime Departure { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime Arrival { get; set; }

    public int Stops { get; set; }

    public decimal OriginalPrice { get; set; }

    public required string OriginalCurrency { get; set; }

    public long PriceIqd { get; set; }

    public int Adults { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
