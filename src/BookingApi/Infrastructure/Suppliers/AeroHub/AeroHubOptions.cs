namespace BookingApi.Infrastructure.Suppliers.AeroHub;

public sealed class AeroHubOptions
{
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public int BookTimeoutSeconds { get; set; } = 15;
}
