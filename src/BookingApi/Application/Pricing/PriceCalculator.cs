using Microsoft.Extensions.Options;

namespace BookingApi.Application.Pricing;

public sealed class PriceCalculator
{
    private readonly IReadOnlyDictionary<string, decimal> _ratesToIqd;

    public PriceCalculator(IOptions<PricingOptions> options)
    {
        _ratesToIqd = options.Value.RatesToIqd;
    }

    public long ToIqd(decimal amount, string currency)
    {
        if (!_ratesToIqd.TryGetValue(currency, out var rate))
        {
            throw new InvalidOperationException($"No exchange rate configured for {currency}.");
        }

        return RoundUpToNextThousand(amount * rate);
    }

    public static long RoundUpToNextThousand(decimal iqd) =>
        (long)(Math.Ceiling(iqd / 1000m) * 1000m);
}
