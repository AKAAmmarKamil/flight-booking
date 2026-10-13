namespace BookingApi.Application.Suppliers;

public sealed record SearchRequest(string From, string To, DateOnly Date, int Adults);
