using BookingApi.Application.Pricing;
using Microsoft.Extensions.Options;

namespace BookingApi.Tests.Pricing;

public class PriceCalculatorTests
{
    private readonly PriceCalculator _calculator = new(Options.Create(new PricingOptions
    {
        RatesToIqd = new() { ["USD"] = 1550m, ["EUR"] = 1680m }
    }));

    public static TheoryData<decimal, long> SpecRoundingCases => new()
    {
        { 454_305m, 455_000 },
        { 455_000m, 455_000 },
        { 455_000.01m, 456_000 }
    };

    [Theory]
    [MemberData(nameof(SpecRoundingCases))]
    public void Rounding_rounds_up_to_next_1000(decimal iqd, long expected)
    {
        Assert.Equal(expected, PriceCalculator.RoundUpToNextThousand(iqd));
    }

    [Fact]
    public void Rounding_converts_AeroHub_sample_293_10_USD_to_455000_IQD()
    {
        Assert.Equal(455_000, _calculator.ToIqd(293.10m, "USD"));
    }

    [Fact]
    public void Rounding_converts_SkyLink_sample_265_40_EUR_to_446000_IQD()
    {
        Assert.Equal(446_000, _calculator.ToIqd(265.40m, "EUR"));
    }

    [Fact]
    public void Rounding_throws_for_currency_without_configured_rate()
    {
        Assert.Throws<InvalidOperationException>(() => _calculator.ToIqd(100m, "GBP"));
    }
}
