namespace BookingApi.Domain;

public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;
}
