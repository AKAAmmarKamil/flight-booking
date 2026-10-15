using BookingApi.Application.Pricing;
using BookingApi.Application.Search;
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

        services.AddOptions<SearchOptions>()
            .BindConfiguration("Search")
            .Validate(options => options.TimeoutSeconds > 0, "Search:TimeoutSeconds must be positive.")
            .ValidateOnStart();

        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<ISearchService, SearchService>();

        return services;
    }
}
