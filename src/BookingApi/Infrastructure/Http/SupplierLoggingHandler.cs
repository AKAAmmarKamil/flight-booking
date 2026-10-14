using System.Diagnostics;
using System.Text;

namespace BookingApi.Infrastructure.Http;

public sealed class SupplierLoggingHandler : DelegatingHandler
{
    private const string CompletedMessage = "{Supplier} {Method} {Url} -> {StatusCode} in {ElapsedMs} ms. Request: {RequestBody} Response: {ResponseBody}";
    private const string FailedMessage = "{Supplier} {Method} {Url} failed after {ElapsedMs} ms. Request: {RequestBody}";

    private readonly string _supplierName;
    private readonly IReadOnlySet<string> _sensitiveFields;
    private readonly ILogger<SupplierLoggingHandler> _logger;

    public SupplierLoggingHandler(string supplierName, IReadOnlySet<string> sensitiveFields, ILogger<SupplierLoggingHandler> logger)
    {
        _supplierName = supplierName;
        _sensitiveFields = sensitiveFields;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var requestBody = await DescribeBodyAsync(request.Content, cancellationToken);

        HttpResponseMessage? response = null;
        string responseBody;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            responseBody = await DescribeBodyAsync(response.Content, cancellationToken);
        }
        catch (Exception exception)
        {
            response?.Dispose();
            var failedAfterMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _logger.LogWarning(exception, FailedMessage, _supplierName, request.Method, request.RequestUri, failedAfterMs, requestBody);
            throw;
        }

        var elapsedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation(CompletedMessage, _supplierName, request.Method, request.RequestUri, (int)response.StatusCode, elapsedMs, requestBody, responseBody);
        return response;
    }

    private async Task<string> DescribeBodyAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if(content == null)
        {
            return "(none)";
        }
        var body = await content.ReadAsStringAsync(cancellationToken);
        var result = JsonRedactor.Redact(body, _sensitiveFields);
        return result == null ?
        $"[non-JSON body, {Encoding.UTF8.GetByteCount(body)} bytes, {content.Headers.ContentType?.MediaType}]" :
        result;
    }
}
