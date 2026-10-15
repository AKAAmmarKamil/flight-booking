using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BookingApi.Tests.Persistence;

public sealed class BookingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public BookingApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: that would load this machine's user-secrets, and the tests must not depend on them.
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["AeroHub:Username"] = "test-user",
            ["AeroHub:Password"] = "test-password",
            ["SkyLink:PartnerId"] = "test-partner",
            ["SkyLink:PartnerKey"] = "test-key"
        }));
    }
}
