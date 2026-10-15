using BookingApi.Application.Pricing;
using BookingApi.Application.Wallets;

namespace BookingApi.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<PricingOptions>()
            .BindConfiguration("Pricing")
            .Validate(options => options.RatesToIqd.Count > 0 && options.RatesToIqd.Values.All(rate => rate > 0), "Pricing:RatesToIqd must contain positive rates.")
            .ValidateOnStart();
        services.AddSingleton<PriceCalculator>();

        services.AddScoped<IWalletService, WalletService>();

        return services;
    }
}
