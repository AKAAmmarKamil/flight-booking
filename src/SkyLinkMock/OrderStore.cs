namespace SkyLinkMock;

public static class OrderStatus
{
    public const string ToBePaid = "TO_BE_PAID";
    public const string Issuing = "ISSUING";
    public const string Issued = "ISSUED";
}

public sealed record Order(string OrderNum, string Status, DateTimeOffset? IssuesAt = null, string? Pnr = null, string? TicketNumber = null)
{
    public OrderData ToData() => new(OrderNum, Status, Pnr, TicketNumber);
}

public sealed class OrderStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Order> _orders = new();
    private readonly HashSet<string> _buyerOrders = new();
    private long _lastOrderNum = 917000000000;

    public Order? Create(string buyerOrder)
    {
        lock (_lock)
        {
            if (!_buyerOrders.Add(buyerOrder))
            {
                return null;
            }

            _lastOrderNum++;
            var order = new Order(_lastOrderNum.ToString(), OrderStatus.ToBePaid);
            _orders[order.OrderNum] = order;
            return order;
        }
    }

    public Order? Find(string orderNum)
    {
        lock (_lock)
        {
            if (!_orders.TryGetValue(orderNum, out var order))
            {
                return null;
            }

            if (order.Status == OrderStatus.Issuing && order.IssuesAt <= DateTimeOffset.UtcNow)
            {
                order = order with { Status = OrderStatus.Issued, Pnr = FakeData.Pnr(), TicketNumber = FakeData.TicketNumber() };
                _orders[orderNum] = order;
            }

            return order;
        }
    }

    public void Update(Order order)
    {
        lock (_lock)
        {
            _orders[order.OrderNum] = order;
        }
    }
}
