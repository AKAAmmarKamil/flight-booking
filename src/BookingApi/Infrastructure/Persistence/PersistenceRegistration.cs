using Microsoft.EntityFrameworkCore;

namespace BookingApi.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddDbContext<BookingDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
            }

            ConfigureDbContext(options, connectionString);
        });

        return services;
    }

    public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }
}
