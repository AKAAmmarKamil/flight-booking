using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SkyLinkMock;

var builder = WebApplication.CreateBuilder(args);

var partnerId = builder.Configuration["Auth:PartnerId"]
    ?? throw new InvalidOperationException("Auth:PartnerId is not configured.");
var partnerKey = builder.Configuration["Auth:PartnerKey"]
    ?? throw new InvalidOperationException("Auth:PartnerKey is not configured.");
var expectedSign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(partnerId + partnerKey))).ToLowerInvariant();

builder.Services.AddSingleton<OrderStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

var app = builder.Build();

bool IsSigned(Auth? auth) =>
    auth is not null
    && auth.PartnerId == partnerId
    && string.Equals(auth.Sign, expectedSign, StringComparison.OrdinalIgnoreCase);

IResult InvalidSign() => Reply.Error(ErrorCodes.InvalidSign, "invalid sign");

IResult OrderNotFound() => Reply.Error(ErrorCodes.OrderNotFound, "order not found");

app.MapPost("/shopping", async (ShoppingRequest request, CancellationToken ct) =>
{
    if (!IsSigned(request.Auth))
    {
        return InvalidSign();
    }

    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(200, 1801)), ct);
    return Reply.Ok(FakeData.Shopping(request.Search));
});

app.MapPost("/order", (OrderRequest request, OrderStore orders) =>
{
    if (!IsSigned(request.Auth))
    {
        return InvalidSign();
    }

    if (Random.Shared.Next(100) < 10)
    {
        return Reply.Error(ErrorCodes.FareNotAvailable, "fare no longer available");
    }

    var order = orders.Create(request.BuyerOrder);
    return order is null
        ? Reply.Error(ErrorCodes.DuplicateOrder, "duplicate order")
        : Reply.Ok(order.ToData());
});

app.MapPost("/ticketing", (TicketingRequest request, OrderStore orders) =>
{
    if (!IsSigned(request.Auth))
    {
        return InvalidSign();
    }

    var order = orders.Find(request.OrderNum);
    if (order is null)
    {
        return OrderNotFound();
    }

    if (order.Status != OrderStatus.ToBePaid)
    {
        return Reply.Ok(order.ToData());
    }

    if (Random.Shared.Next(100) < 10)
    {
        return Reply.Error(ErrorCodes.PrepaidBalanceTooLow, "prepaid balance too low");
    }

    var issuing = order with
    {
        Status = OrderStatus.Issuing,
        IssuesAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(10, 31))
    };
    orders.Update(issuing);
    return Reply.Ok(issuing.ToData());
});

app.MapGet("/order/{orderNum}", (string orderNum, OrderStore orders) =>
    orders.Find(orderNum) is { } order ? Reply.Ok(order.ToData()) : OrderNotFound());

app.Run();
