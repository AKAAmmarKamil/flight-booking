using System.Text.Json.Serialization;
using BookingApi.Application;
using BookingApi.Infrastructure.BackgroundJobs;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Infrastructure.Suppliers.AeroHub;
using BookingApi.Infrastructure.Suppliers.SkyLink;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplicationServices();
builder.Services.AddPersistence();
builder.Services.AddAeroHubSupplier();
builder.Services.AddSkyLinkSupplier();
builder.Services.AddHostedService<PendingBookingPoller>();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.MigrateDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
