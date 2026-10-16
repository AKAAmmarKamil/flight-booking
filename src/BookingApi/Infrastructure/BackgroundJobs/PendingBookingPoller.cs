using BookingApi.Application.Bookings;
using Microsoft.Extensions.Options;

namespace BookingApi.Infrastructure.BackgroundJobs;

public class PendingBookingPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PendingPollerOptions _options;
    private readonly ILogger<PendingBookingPoller> _logger;

    public PendingBookingPoller(
        IServiceScopeFactory scopeFactory,
        IOptions<PendingPollerOptions> options,
        ILogger<PendingBookingPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<PendingBookingProcessor>();
                await processor.ProcessOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An exception escaping ExecuteAsync stops the whole host; the next tick tries again instead.
                _logger.LogError(exception, "Pending booking check failed.");
            }
        }
    }
}
