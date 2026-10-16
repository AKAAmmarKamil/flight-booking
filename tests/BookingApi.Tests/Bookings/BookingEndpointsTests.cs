using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApi.Application.Bookings;
using BookingApi.Application.Suppliers;
using BookingApi.Application.Wallets;
using BookingApi.Domain;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Tests.Fakes;
using BookingApi.Tests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingApi.Tests.Bookings;

[Collection(PostgresCollection.Name)]
public class BookingEndpointsTests
{
    private const long OfferPriceIqd = 455_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _postgres;

    public BookingEndpointsTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Booking_two_identical_requests_with_same_key_debit_once_and_create_one_booking()
    {
        var aeroHub = AeroHubAnsweringBookWith(_ => Task.FromResult(AeroHubConfirmed()));
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");
        var idempotencyKey = UniqueKey();

        var first = await ReadBookingResultAsync(await BookAsync(client, idempotencyKey, Form(userId, offer.Id)));
        var second = await ReadBookingResultAsync(await BookAsync(client, idempotencyKey, Form(userId, offer.Id)));

        Assert.Equal(first.BookingId, second.BookingId);
        Assert.Equal(BookingStatus.Confirmed, second.Status);
        Assert.Equal(1, aeroHub.CallsTo("/book"));
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(1, await dbContext.Bookings.CountAsync(booking => booking.IdempotencyKey == idempotencyKey));
        Assert.Equal(1, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(1_000_000 - OfferPriceIqd, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_concurrent_requests_with_same_key_debit_once_and_book_once()
    {
        const int requestCount = 10;
        var aeroHub = AeroHubAnsweringBookWith(async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            return AeroHubConfirmed();
        });
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");
        var idempotencyKey = UniqueKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, requestCount)
            .Select(_ => BookAsync(client, idempotencyKey, Form(userId, offer.Id))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var results = await Task.WhenAll(responses.Select(ReadBookingResultAsync));
        Assert.Single(results.Select(result => result.BookingId).Distinct());
        Assert.Equal(1, aeroHub.CallsTo("/book"));
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(1, await dbContext.Bookings.CountAsync(booking => booking.IdempotencyKey == idempotencyKey));
        Assert.Equal(1, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(1_000_000 - OfferPriceIqd, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_same_key_with_different_body_returns_409()
    {
        await using var factory = CreateFactory(aeroHub: AeroHubAnsweringBookWith(_ => Task.FromResult(AeroHubConfirmed())));
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");
        var idempotencyKey = UniqueKey();
        await ReadBookingResultAsync(await BookAsync(client, idempotencyKey, Form(userId, offer.Id)));

        var otherPassenger = Form(userId, offer.Id);
        otherPassenger.Passenger.FirstName = "OMAR";
        var response = await BookAsync(client, idempotencyKey, otherPassenger);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(1, await CountDebitsAsync(dbContext, userId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Booking_without_idempotency_key_returns_400(string? idempotencyKey)
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");

        var response = await BookAsync(client, idempotencyKey, Form(userId, offer.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(0, await CountDebitsAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_insufficient_balance_returns_402_with_no_booking_and_no_debit()
    {
        var aeroHub = AeroHubAnsweringBookWith(_ => Task.FromResult(AeroHubConfirmed()));
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, OfferPriceIqd - 1_000);
        var offer = await SaveOfferAsync("AeroHub");
        var idempotencyKey = UniqueKey();

        var response = await BookAsync(client, idempotencyKey, Form(userId, offer.Id));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal(0, aeroHub.CallsTo("/book"));
        await using var dbContext = _postgres.CreateDbContext();
        Assert.False(await dbContext.Bookings.AnyAsync(booking => booking.IdempotencyKey == idempotencyKey));
        Assert.Equal(0, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(OfferPriceIqd - 1_000, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_concurrent_bookings_for_one_user_never_take_the_balance_below_zero()
    {
        await using var factory = CreateFactory(aeroHub: AeroHubAnsweringBookWith(_ => Task.FromResult(AeroHubConfirmed())));
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, OfferPriceIqd);
        var offer = await SaveOfferAsync("AeroHub");

        var responses = await Task.WhenAll(
            BookAsync(client, UniqueKey(), Form(userId, offer.Id)),
            BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.PaymentRequired },
            responses.Select(response => response.StatusCode).OrderBy(statusCode => statusCode));
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(1, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(0, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_unknown_offer_returns_404_and_debits_nothing()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);

        var response = await BookAsync(client, UniqueKey(), Form(userId, "AH-does-not-exist"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(0, await CountDebitsAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_expired_offer_returns_410_and_debits_nothing()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await BookAsync(client, UniqueKey(), Form(userId, offer.Id));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        await using var dbContext = _postgres.CreateDbContext();
        Assert.Equal(0, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(1_000_000, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_AeroHub_CONFIRMED_becomes_Confirmed_with_pnr_and_ticket()
    {
        string? bookRequestBody = null;
        var aeroHub = AeroHubAnsweringBookWith(async (request, _) =>
        {
            bookRequestBody = await request.Content!.ReadAsStringAsync();
            return AeroHubConfirmed();
        });
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");

        var result = await ReadBookingResultAsync(await BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        Assert.Equal(BookingStatus.Confirmed, result.Status);
        Assert.Equal(OfferPriceIqd, result.PriceIqd);
        Assert.Equal("X7K2LM", result.Pnr);
        Assert.Equal("2351234567890", result.TicketNumber);
        Assert.NotNull(bookRequestBody);
        Assert.Contains(offer.SupplierReference, bookRequestBody);
        await using var dbContext = _postgres.CreateDbContext();
        var history = await GetHistoryAsync(dbContext, result.BookingId);
        Assert.Equal(
            new BookingStatus?[] { null, BookingStatus.Created, BookingStatus.Paid },
            history.Select(change => change.FromStatus));
        Assert.Equal(
            new[] { BookingStatus.Created, BookingStatus.Paid, BookingStatus.Confirmed },
            history.Select(change => change.ToStatus));
    }

    [Fact]
    public async Task Booking_AeroHub_FAILED_requires_review_and_is_not_refunded()
    {
        var aeroHub = AeroHubAnsweringBookWith(_ => Task.FromResult(Json("""{ "status": "FAILED", "reason": "SOLD_OUT" }""")));
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");

        var response = await BookAsync(client, UniqueKey(), Form(userId, offer.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadBookingResultAsync(response);
        Assert.Equal(BookingStatus.RequiresReview, result.Status);
        Assert.Null(result.TicketNumber);
        Assert.Equal(1, aeroHub.CallsTo("/book"));
        await using var dbContext = _postgres.CreateDbContext();
        var lastChange = (await GetHistoryAsync(dbContext, result.BookingId)).Last();
        Assert.Contains("SOLD_OUT", lastChange.Note);
        Assert.Contains("no ticket was issued", lastChange.Note);
        Assert.Equal(1_000_000 - OfferPriceIqd, await GetBalanceAsync(dbContext, userId));
        Assert.Equal(
            new[] { WalletTransactionType.Deposit, WalletTransactionType.Debit },
            await dbContext.WalletTransactions
                .Where(walletTransaction => walletTransaction.UserId == userId)
                .OrderBy(walletTransaction => walletTransaction.Id)
                .Select(walletTransaction => walletTransaction.Type)
                .ToListAsync());
    }

    [Fact]
    public async Task Booking_AeroHub_timeout_requires_review_with_may_exist_note()
    {
        var aeroHub = AeroHubAnsweringBookWith(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return AeroHubConfirmed();
        });
        await using var factory = CreateFactory(aeroHub: aeroHub, aeroHubBookTimeoutSeconds: 1);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");

        var result = await ReadBookingResultAsync(await BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        Assert.Equal(BookingStatus.RequiresReview, result.Status);
        Assert.Equal(1, aeroHub.CallsTo("/book"));
        await using var dbContext = _postgres.CreateDbContext();
        var lastChange = (await GetHistoryAsync(dbContext, result.BookingId)).Last();
        Assert.Equal(BookingStatus.Paid, lastChange.FromStatus);
        Assert.Contains("a ticket may have been issued; check with AeroHub", lastChange.Note);
        Assert.Equal(1, await CountDebitsAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_SkyLink_ISSUING_becomes_Pending_then_job_makes_it_Confirmed()
    {
        var skyLink = SkyLinkAnsweringTicketingWith(
            """{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "ISSUING" } }""",
            """{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "ISSUED", "pnr": "Q8W2ER", "ticketNumber": "6071234567890" } }""");
        await using var factory = CreateFactory(skyLink: skyLink);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("SkyLink");

        var result = await ReadBookingResultAsync(await BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        Assert.Equal(BookingStatus.Pending, result.Status);
        Assert.Null(result.TicketNumber);
        await using var dbContext = _postgres.CreateDbContext();
        var pendingBooking = await dbContext.Bookings.SingleAsync(saved => saved.Id == result.BookingId);
        Assert.Equal("917000000001", pendingBooking.SupplierReference);
        Assert.Equal("SkyLink order 917000000001 is ISSUING", (await GetHistoryAsync(dbContext, result.BookingId)).Last().Note);

        await RunPendingJobOnceAsync(factory);

        var booking = await client.GetFromJsonAsync<BookingDto>($"/bookings/{result.BookingId}", JsonOptions);
        Assert.NotNull(booking);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal("Q8W2ER", booking.Pnr);
        Assert.Equal("6071234567890", booking.TicketNumber);
        Assert.Equal(4, booking.History.Count);
        var confirmed = booking.History.Last();
        Assert.Equal(BookingStatus.Pending, confirmed.From);
        Assert.Equal(BookingStatus.Confirmed, confirmed.To);
        Assert.Equal("SkyLink order 917000000001 is ISSUED.", confirmed.Note);
    }

    [Fact]
    public async Task Booking_SkyLink_B022_requires_review()
    {
        var skyLink = SkyLinkAnsweringTicketingWith("""{ "errorCode": "B022", "errorMsg": "prepaid balance too low" }""");
        await using var factory = CreateFactory(skyLink: skyLink);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("SkyLink");

        var result = await ReadBookingResultAsync(await BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        Assert.Equal(BookingStatus.RequiresReview, result.Status);
        await using var dbContext = _postgres.CreateDbContext();
        var booking = await dbContext.Bookings.SingleAsync(saved => saved.Id == result.BookingId);
        Assert.Equal("917000000001", booking.SupplierReference);
        var lastChange = (await GetHistoryAsync(dbContext, result.BookingId)).Last();
        Assert.Contains("B022", lastChange.Note);
        Assert.Contains("no ticket was issued", lastChange.Note);
        Assert.Equal(1_000_000 - OfferPriceIqd, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task Booking_unexpected_supplier_exception_requires_review()
    {
        var aeroHub = AeroHubAnsweringBookWith(_ => throw new InvalidOperationException("Unexpected failure inside the HTTP pipeline."));
        await using var factory = CreateFactory(aeroHub: aeroHub);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("AeroHub");

        var response = await BookAsync(client, UniqueKey(), Form(userId, offer.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadBookingResultAsync(response);
        Assert.Equal(BookingStatus.RequiresReview, result.Status);
        await using var dbContext = _postgres.CreateDbContext();
        var lastChange = (await GetHistoryAsync(dbContext, result.BookingId)).Last();
        Assert.Contains("InvalidOperationException", lastChange.Note);
        Assert.Contains("a ticket may have been issued; check with AeroHub", lastChange.Note);
    }

    [Fact]
    public async Task Booking_offer_of_unregistered_supplier_fails_before_any_debit()
    {
        await using var factory = _postgres.ApiFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFlightSupplier>();
            services.AddSingleton<IFlightSupplier>(new FakeFlightSupplier("AeroHub", "AH", TimeSpan.Zero, []));
        }));
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("SkyLink");
        var idempotencyKey = UniqueKey();

        // TestServer hands an unhandled exception to the caller; a real server answers 500.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => BookAsync(client, idempotencyKey, Form(userId, offer.Id)));

        Assert.Contains("No supplier named 'SkyLink'", exception.Message);
        await using var dbContext = _postgres.CreateDbContext();
        Assert.False(await dbContext.Bookings.AnyAsync(booking => booking.IdempotencyKey == idempotencyKey));
        Assert.Equal(0, await CountDebitsAsync(dbContext, userId));
        Assert.Equal(1_000_000, await GetBalanceAsync(dbContext, userId));
    }

    [Fact]
    public async Task GetBooking_returns_status_history_in_order()
    {
        var skyLink = SkyLinkAnsweringTicketingWith(
            """{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "ISSUING" } }""");
        await using var factory = CreateFactory(skyLink: skyLink);
        var client = factory.CreateClient();
        var userId = await CreateWalletAsync(client, 1_000_000);
        var offer = await SaveOfferAsync("SkyLink");
        var result = await ReadBookingResultAsync(await BookAsync(client, UniqueKey(), Form(userId, offer.Id)));

        var booking = await client.GetFromJsonAsync<BookingDto>($"/bookings/{result.BookingId}", JsonOptions);

        Assert.NotNull(booking);
        Assert.Equal(result.BookingId, booking.BookingId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(OfferPriceIqd, booking.PriceIqd);
        Assert.Equal("SkyLink", booking.Supplier);
        Assert.Collection(booking.History,
            created =>
            {
                Assert.Null(created.From);
                Assert.Equal(BookingStatus.Created, created.To);
            },
            paid =>
            {
                Assert.Equal(BookingStatus.Created, paid.From);
                Assert.Equal(BookingStatus.Paid, paid.To);
                Assert.Equal($"Wallet debited {OfferPriceIqd} IQD", paid.Note);
            },
            pending =>
            {
                Assert.Equal(BookingStatus.Paid, pending.From);
                Assert.Equal(BookingStatus.Pending, pending.To);
                Assert.Equal("SkyLink order 917000000001 is ISSUING", pending.Note);
            });
    }

    [Fact]
    public async Task GetBooking_unknown_id_returns_404()
    {
        var client = _postgres.ApiFactory.CreateClient();

        var response = await client.GetAsync("/bookings/b-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(
        StubHttpMessageHandler? aeroHub = null,
        StubHttpMessageHandler? skyLink = null,
        int aeroHubBookTimeoutSeconds = 15) =>
        _postgres.ApiFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AeroHub:BookTimeoutSeconds"] = aeroHubBookTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
            }));
            builder.ConfigureTestServices(services =>
            {
                // Replaces only the innermost handler: the real adapters and the logging handler still run.
                services.AddHttpClient("AeroHub").ConfigurePrimaryHttpMessageHandler(() => aeroHub ?? UnexpectedCalls());
                services.AddHttpClient("SkyLink").ConfigurePrimaryHttpMessageHandler(() => skyLink ?? UnexpectedCalls());
            });
        });

    private static StubHttpMessageHandler AeroHubAnsweringBookWith(Func<CancellationToken, Task<HttpResponseMessage>> bookResponse) =>
        AeroHubAnsweringBookWith((_, cancellationToken) => bookResponse(cancellationToken));

    private static StubHttpMessageHandler AeroHubAnsweringBookWith(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> bookResponse) =>
        new((request, cancellationToken) => request.RequestUri!.AbsolutePath switch
        {
            "/token" => Task.FromResult(Json("""{ "access_token": "token-1", "expires_in": 600 }""")),
            "/book" => bookResponse(request, cancellationToken),
            _ => throw new InvalidOperationException($"Unexpected call to {request.RequestUri}")
        });

    private static StubHttpMessageHandler SkyLinkAnsweringTicketingWith(string ticketingBody, string? orderStatusBody = null) =>
        new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/order" => Task.FromResult(Json("""{ "errorCode": "0", "data": { "orderNum": "917000000001", "status": "TO_BE_PAID" } }""")),
            "/ticketing" => Task.FromResult(Json(ticketingBody)),
            "/order/917000000001" when orderStatusBody != null => Task.FromResult(Json(orderStatusBody)),
            _ => throw new InvalidOperationException($"Unexpected call to {request.RequestUri}")
        });

    private static async Task RunPendingJobOnceAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PendingBookingProcessor>().ProcessOnceAsync(CancellationToken.None);
    }

    private static StubHttpMessageHandler UnexpectedCalls() =>
        new((request, _) => throw new InvalidOperationException($"Unexpected call to {request.RequestUri}"));

    private static HttpResponseMessage AeroHubConfirmed() =>
        Json("""{ "status": "CONFIRMED", "pnr": "X7K2LM", "ticket_number": "2351234567890" }""");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private async Task<Offer> SaveOfferAsync(string supplier, DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var offer = new Offer
        {
            Id = $"{supplier}-{Guid.NewGuid():N}",
            Supplier = supplier,
            SupplierReference = $"ref-{Guid.NewGuid():N}",
            Airline = "TK",
            FlightNumber = "TK785",
            From = "BGW",
            To = "IST",
            Departure = new DateTime(2026, 11, 20, 8, 30, 0),
            Arrival = new DateTime(2026, 11, 20, 11, 0, 0),
            Stops = 0,
            OriginalPrice = 293.10m,
            OriginalCurrency = "USD",
            PriceIqd = OfferPriceIqd,
            Adults = 1,
            CreatedAt = now,
            ExpiresAt = expiresAt ?? now.AddMinutes(30)
        };

        await using var dbContext = _postgres.CreateDbContext();
        dbContext.Offers.Add(offer);
        await dbContext.SaveChangesAsync();
        return offer;
    }

    private static async Task<string> CreateWalletAsync(HttpClient client, long balanceIqd)
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync($"/wallets/{userId}/deposit", new DepositForm { AmountIqd = balanceIqd });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return userId;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, string? idempotencyKey, BookingForm form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bookings") { Content = JsonContent.Create(form) };
        if (idempotencyKey != null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    private static async Task<BookingResultDto> ReadBookingResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BookingResultDto>(JsonOptions);
        Assert.NotNull(result);
        return result;
    }

    private static BookingForm Form(string userId, string offerId) =>
        new()
        {
            UserId = userId,
            OfferId = offerId,
            Passenger = new PassengerForm { FirstName = "ALI", LastName = "HASSAN", BirthDate = new DateOnly(1990, 1, 15) }
        };

    private static Task<List<BookingStatusHistory>> GetHistoryAsync(BookingDbContext dbContext, string bookingId) =>
        dbContext.BookingStatusHistories
            .Where(change => change.BookingId == bookingId)
            .OrderBy(change => change.At)
            .ThenBy(change => change.Id)
            .ToListAsync();

    private static Task<int> CountDebitsAsync(BookingDbContext dbContext, string userId) =>
        dbContext.WalletTransactions.CountAsync(walletTransaction =>
            walletTransaction.UserId == userId && walletTransaction.Type == WalletTransactionType.Debit);

    private static async Task<long> GetBalanceAsync(BookingDbContext dbContext, string userId) =>
        (await dbContext.Wallets.SingleAsync(wallet => wallet.UserId == userId)).BalanceIqd;

    private static string UniqueKey() => $"key-{Guid.NewGuid():N}";
}
