using BookingApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BookingApi.Tests.Persistence;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public BookingApiFactory ApiFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        ApiFactory = new BookingApiFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await ApiFactory.DisposeAsync();
        await _container.DisposeAsync();
    }

    public BookingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>();
        PersistenceRegistration.ConfigureDbContext(options, ConnectionString);
        return new BookingDbContext(options.Options);
    }
}
