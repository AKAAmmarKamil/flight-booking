using BookingApi.Application.Suppliers;
using Microsoft.Extensions.Options;

namespace BookingApi.Infrastructure.Suppliers.SkyLink;

public static class SkyLinkRegistration
{
    public static IServiceCollection AddSkyLinkSupplier(this IServiceCollection services)
    {
        services.AddOptions<SkyLinkOptions>()
            .BindConfiguration("SkyLink")
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "SkyLink:BaseUrl must be an absolute URL.")
            .Validate(options => options.PartnerId.Length > 0 && options.PartnerKey.Length > 0, "SkyLink:PartnerId and SkyLink:PartnerKey must be configured.")
            .Validate(options => options.BookTimeoutSeconds > 0, "SkyLink:BookTimeoutSeconds must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IFlightSupplier, SkyLinkSupplier>(SkyLinkSupplier.SupplierName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SkyLinkOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        return services;
    }
}
