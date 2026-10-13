namespace BookingApi.Application.Pricing;

public sealed class PricingOptions
{
    public Dictionary<string, decimal> RatesToIqd { get; set; } = new();
}
