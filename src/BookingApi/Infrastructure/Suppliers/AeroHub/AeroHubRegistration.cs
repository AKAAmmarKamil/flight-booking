using BookingApi.Application.Suppliers;
using BookingApi.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace BookingApi.Infrastructure.Suppliers.AeroHub;

public static class AeroHubRegistration
{
    private static readonly IReadOnlySet<string> SensitiveFields =
        new HashSet<string>(["password", "access_token"], StringComparer.OrdinalIgnoreCase);

    public static IServiceCollection AddAeroHubSupplier(this IServiceCollection services)
    {
        services.AddOptions<AeroHubOptions>()
            .BindConfiguration("AeroHub")
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "AeroHub:BaseUrl must be an absolute URL.")
            .Validate(options => options.Username.Length > 0 && options.Password.Length > 0, "AeroHub:Username and AeroHub:Password must be configured.")
            .Validate(options => options.BookTimeoutSeconds > 0, "AeroHub:BookTimeoutSeconds must be positive.")
            .ValidateOnStart();

        services.AddSingleton<AeroHubTokenCache>();
        services.AddHttpClient<IFlightSupplier, AeroHubSupplier>(AeroHubSupplier.SupplierName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AeroHubOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        })
        .AddHttpMessageHandler(serviceProvider => new SupplierLoggingHandler(
            AeroHubSupplier.SupplierName,
            SensitiveFields,
            serviceProvider.GetRequiredService<ILogger<SupplierLoggingHandler>>()));

        return services;
    }
}
