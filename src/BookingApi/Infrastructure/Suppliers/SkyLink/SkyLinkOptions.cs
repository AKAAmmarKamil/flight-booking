namespace BookingApi.Infrastructure.Suppliers.SkyLink;

public sealed class SkyLinkOptions
{
    public string BaseUrl { get; set; } = "";
    public string PartnerId { get; set; } = "";
    public string PartnerKey { get; set; } = "";
    public int BookTimeoutSeconds { get; set; } = 15;
}
