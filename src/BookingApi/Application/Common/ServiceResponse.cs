namespace BookingApi.Application.Common;

public class ServiceResponse<T>
{
    public ServiceResponse(T data)
    {
        Data = data;
    }

    public ServiceResponse(ServiceError error, string message)
    {
        Error = error;
        ServiceMessage = message;
    }

    public ServiceError? Error { get; }

    public string? ServiceMessage { get; }

    public T? Data { get; }
}
