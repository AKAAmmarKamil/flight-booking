using System.Globalization;
using System.Net;
using System.Text;
using BookingApi.Application.Bookings;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Tests.Fakes;
using BookingApi.Tests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BookingApi.Tests.Bookings;

[Collection(PostgresCollection.Name)]
public class PendingBookingProcessorTests
{
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(5);

    private readonly PostgresFixture _postgres;
    // Whole seconds: PostgreSQL keeps microseconds, so a start with finer ticks would come back slightly earlier and blur the 5-minute boundary.
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public PendingBookingProcessorTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task PendingJob_still_issuing_before_5_minutes_stays_Pending_without_new_history()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum, Issuing(orderNum)));
        var booking = await SavePendingBookingAsync(orderNum);

        _time.Advance(PendingTimeout - TimeSpan.FromSeconds(1));
        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        var saved = await dbContext.Bookings.SingleAsync(saved => saved.Id == booking.Id);
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Equal(booking.StatusChangedAt, saved.StatusChangedAt);
        Assert.Empty(await GetHistoryAsync(dbContext, booking.Id));
    }

    [Fact]
    public async Task PendingJob_still_issuing_after_5_minutes_requires_review_with_may_exist_note()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum, Issuing(orderNum)));
        var booking = await SavePendingBookingAsync(orderNum);

        _time.Advance(PendingTimeout);
        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.RequiresReview, await GetStatusAsync(dbContext, booking.Id));
        var change = Assert.Single(await GetHistoryAsync(dbContext, booking.Id));
        Assert.Equal(BookingStatus.Pending, change.FromStatus);
        Assert.Equal(BookingStatus.RequiresReview, change.ToStatus);
        Assert.Equal(_time.GetUtcNow(), change.At);
        Assert.Contains($"SkyLink order {orderNum} still not issued", change.Note);
        Assert.Contains("a ticket may still be issued; check with SkyLink", change.Note);
    }

    [Fact]
    public async Task PendingJob_issued_after_5_minutes_is_Confirmed_not_review()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum, Issued(orderNum)));
        var booking = await SavePendingBookingAsync(orderNum);

        _time.Advance(PendingTimeout + TimeSpan.FromMinutes(1));
        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        var saved = await dbContext.Bookings.SingleAsync(saved => saved.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, saved.Status);
        Assert.Equal("Q8W2ER", saved.Pnr);
        Assert.Equal("6071234567890", saved.TicketNumber);
    }

    [Fact]
    public async Task PendingJob_failed_status_call_keeps_booking_Pending()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum, OrderNotFound()));
        var booking = await SavePendingBookingAsync(orderNum);

        _time.Advance(TimeSpan.FromSeconds(10));
        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, await GetStatusAsync(dbContext, booking.Id));
        Assert.Empty(await GetHistoryAsync(dbContext, booking.Id));
    }

    [Fact]
    public async Task PendingJob_failed_status_call_after_5_minutes_requires_review()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum, OrderNotFound()));
        var booking = await SavePendingBookingAsync(orderNum);

        _time.Advance(PendingTimeout);
        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.RequiresReview, await GetStatusAsync(dbContext, booking.Id));
        var change = Assert.Single(await GetHistoryAsync(dbContext, booking.Id));
        Assert.Contains("a ticket may still be issued; check with SkyLink", change.Note);
    }

    [Fact]
    public async Task PendingJob_hanging_status_call_times_out_and_keeps_booking_Pending()
    {
        var orderNum = UniqueOrderNum();
        var skyLink = SkyLinkAnsweringOrder(orderNum, async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json(Issuing(orderNum));
        });
        await using var factory = CreateFactory(skyLink, statusTimeoutSeconds: 1);
        var booking = await SavePendingBookingAsync(orderNum);

        await RunPendingJobOnceAsync(factory);

        Assert.Equal(1, skyLink.CallsTo($"/order/{orderNum}"));
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, await GetStatusAsync(dbContext, booking.Id));
    }

    [Fact]
    public async Task PendingJob_cancelled_order_requires_review()
    {
        var orderNum = UniqueOrderNum();
        await using var factory = CreateFactory(SkyLinkAnsweringOrder(orderNum,
            $$"""{ "errorCode": "0", "data": { "orderNum": "{{orderNum}}", "status": "CANCELLED" } }"""));
        var booking = await SavePendingBookingAsync(orderNum);

        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.RequiresReview, await GetStatusAsync(dbContext, booking.Id));
        var change = Assert.Single(await GetHistoryAsync(dbContext, booking.Id));
        Assert.Contains("has status CANCELLED", change.Note);
    }

    [Fact]
    public async Task PendingJob_one_failing_booking_does_not_stop_the_others()
    {
        var failingOrderNum = UniqueOrderNum();
        var issuedOrderNum = UniqueOrderNum();
        var skyLink = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == $"/order/{failingOrderNum}"
                ? throw new HttpRequestException("Connection reset.")
                : Task.FromResult(Json(request.RequestUri.AbsolutePath == $"/order/{issuedOrderNum}" ? Issued(issuedOrderNum) : OrderNotFound())));
        await using var factory = CreateFactory(skyLink);
        var failingBooking = await SavePendingBookingAsync(failingOrderNum);
        _time.Advance(TimeSpan.FromSeconds(1));
        var issuedBooking = await SavePendingBookingAsync(issuedOrderNum);

        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, await GetStatusAsync(dbContext, failingBooking.Id));
        Assert.Equal(BookingStatus.Confirmed, await GetStatusAsync(dbContext, issuedBooking.Id));
    }

    [Fact]
    public async Task PendingJob_booking_changed_by_another_instance_is_not_overwritten()
    {
        var contestedOrderNum = UniqueOrderNum();
        var nextOrderNum = UniqueOrderNum();
        var contestedBooking = await SavePendingBookingAsync(contestedOrderNum);
        _time.Advance(TimeSpan.FromSeconds(1));
        var nextBooking = await SavePendingBookingAsync(nextOrderNum);
        var skyLink = new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == $"/order/{contestedOrderNum}")
            {
                // Another instance finishes this booking while our status call is still running.
                await using var otherInstance = _postgres.CreateDbContext();
                var booking = await otherInstance.Bookings.SingleAsync(saved => saved.Id == contestedBooking.Id);
                booking.MoveTo(BookingStatus.RequiresReview, "Moved by another instance.", _time.GetUtcNow());
                await otherInstance.SaveChangesAsync();
                return Json(Issued(contestedOrderNum));
            }

            return Json(path == $"/order/{nextOrderNum}" ? Issued(nextOrderNum) : OrderNotFound());
        });
        await using var factory = CreateFactory(skyLink);

        await RunPendingJobOnceAsync(factory);

        await using var dbContext = _postgres.CreateDbContext();
        var contested = await dbContext.Bookings.SingleAsync(saved => saved.Id == contestedBooking.Id);
        Assert.Equal(BookingStatus.RequiresReview, contested.Status);
        Assert.Null(contested.TicketNumber);
        var change = Assert.Single(await GetHistoryAsync(dbContext, contestedBooking.Id));
        Assert.Equal("Moved by another instance.", change.Note);
        Assert.Equal(BookingStatus.Confirmed, await GetStatusAsync(dbContext, nextBooking.Id));
        Assert.Single(await GetHistoryAsync(dbContext, nextBooking.Id));
    }

    private WebApplicationFactory<Program> CreateFactory(StubHttpMessageHandler skyLink, int statusTimeoutSeconds = 10) =>
        _postgres.ApiFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PendingPoller:StatusTimeoutSeconds"] = statusTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_time);
                services.AddHttpClient("SkyLink").ConfigurePrimaryHttpMessageHandler(() => skyLink);
            });
        });

    private static async Task RunPendingJobOnceAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PendingBookingProcessor>().ProcessOnceAsync(CancellationToken.None);
    }

    // Pending bookings left by other tests are processed too; their unknown order numbers answer "not found".
    private static StubHttpMessageHandler SkyLinkAnsweringOrder(string orderNum, string orderBody) =>
        SkyLinkAnsweringOrder(orderNum, _ => Task.FromResult(Json(orderBody)));

    private static StubHttpMessageHandler SkyLinkAnsweringOrder(string orderNum, Func<CancellationToken, Task<HttpResponseMessage>> orderResponse) =>
        new((request, cancellationToken) => request.RequestUri!.AbsolutePath == $"/order/{orderNum}"
            ? orderResponse(cancellationToken)
            : Task.FromResult(Json(OrderNotFound())));

    private static string Issuing(string orderNum) =>
        $$"""{ "errorCode": "0", "data": { "orderNum": "{{orderNum}}", "status": "ISSUING" } }""";

    private static string Issued(string orderNum) =>
        $$"""{ "errorCode": "0", "data": { "orderNum": "{{orderNum}}", "status": "ISSUED", "pnr": "Q8W2ER", "ticketNumber": "6071234567890" } }""";

    private static string OrderNotFound() =>
        """{ "errorCode": "B001", "errorMsg": "order not found" }""";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private async Task<Booking> SavePendingBookingAsync(string orderNum)
    {
        var now = _time.GetUtcNow();
        var offer = new Offer
        {
            Id = $"SL-{Guid.NewGuid():N}",
            Supplier = "SkyLink",
            SupplierReference = $"S-{Guid.NewGuid():N}",
            Airline = "TK",
            FlightNumber = "TK785",
            From = "BGW",
            To = "IST",
            Departure = new DateTime(2026, 11, 20, 8, 30, 0),
            Arrival = new DateTime(2026, 11, 20, 11, 0, 0),
            Stops = 0,
            OriginalPrice = 265.40m,
            OriginalCurrency = "EUR",
            PriceIqd = 446_000,
            Adults = 1,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };
        var booking = new Booking
        {
            Id = $"b-{Guid.NewGuid():N}",
            IdempotencyKey = $"key-{Guid.NewGuid():N}",
            RequestHash = new string('0', 64),
            UserId = $"user-{Guid.NewGuid():N}",
            OfferId = offer.Id,
            Supplier = "SkyLink",
            PriceIqd = offer.PriceIqd,
            PassengerFirstName = "ALI",
            PassengerLastName = "HASSAN",
            PassengerBirthDate = new DateOnly(1990, 1, 15),
            Status = BookingStatus.Pending,
            StatusChangedAt = now,
            SupplierReference = orderNum
        };

        await using var dbContext = _postgres.CreateDbContext();
        dbContext.Offers.Add(offer);
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();
        return booking;
    }

    private static async Task<BookingStatus> GetStatusAsync(BookingDbContext dbContext, string bookingId) =>
        (await dbContext.Bookings.SingleAsync(booking => booking.Id == bookingId)).Status;

    private static Task<List<BookingStatusHistory>> GetHistoryAsync(BookingDbContext dbContext, string bookingId) =>
        dbContext.BookingStatusHistories
            .Where(change => change.BookingId == bookingId)
            .OrderBy(change => change.At)
            .ThenBy(change => change.Id)
            .ToListAsync();

    private static string UniqueOrderNum() => $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
}
