using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHubMock;

var builder = WebApplication.CreateBuilder(args);

var username = builder.Configuration["Auth:Username"]
    ?? throw new InvalidOperationException("Auth:Username is not configured.");
var password = builder.Configuration["Auth:Password"]
    ?? throw new InvalidOperationException("Auth:Password is not configured.");
var tokenLifetime = TimeSpan.FromSeconds(builder.Configuration.GetValue("Auth:TokenLifetimeSeconds", 600));

builder.Services.AddSingleton(new TokenStore(tokenLifetime));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.MapPost("/token", (TokenRequest request, TokenStore tokens) =>
    request.Username == username && request.Password == password
        ? Results.Ok(new TokenResponse(tokens.Issue(), (int)tokens.Lifetime.TotalSeconds))
        : Results.Unauthorized());

var secured = app.MapGroup("").AddEndpointFilter(async (context, next) =>
{
    var tokens = context.HttpContext.RequestServices.GetRequiredService<TokenStore>();
    var header = context.HttpContext.Request.Headers.Authorization.ToString();

    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && tokens.IsValid(header["Bearer ".Length..])
        ? await next(context)
        : Results.Unauthorized();
});

secured.MapPost("/search", async (SearchRequest request, CancellationToken ct) =>
{
    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 8001)), ct);
    return Results.Ok(new SearchResponse(FakeData.Offers(request)));
});

secured.MapPost("/book", async (BookRequest request, CancellationToken ct) =>
{
    var roll = Random.Shared.Next(100);

    if (roll < 20)
    {
        return Results.Ok(new BookResponse("FAILED", Reason: "SOLD_OUT"));
    }

    if (roll < 40)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
    }

    return Results.Ok(new BookResponse("CONFIRMED", FakeData.Pnr(), FakeData.TicketNumber()));
});

app.Run();
