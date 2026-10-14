using BookingApi.Application.Pricing;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Infrastructure.Suppliers.AeroHub;
using BookingApi.Infrastructure.Suppliers.SkyLink;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<PricingOptions>()
    .BindConfiguration("Pricing")
    .Validate(options => options.RatesToIqd.Count > 0 && options.RatesToIqd.Values.All(rate => rate > 0), "Pricing:RatesToIqd must contain positive rates.")
    .ValidateOnStart();
builder.Services.AddSingleton<PriceCalculator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence();
builder.Services.AddAeroHubSupplier();
builder.Services.AddSkyLinkSupplier();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
