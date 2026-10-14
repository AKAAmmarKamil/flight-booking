using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BookingApi.Infrastructure.Http;
using BookingApi.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace BookingApi.Tests.Http;

public class SupplierLoggingHandlerTests
{
    private static readonly IReadOnlySet<string> SensitiveFields =
        new HashSet<string>(["password", "access_token", "sign"], StringComparer.OrdinalIgnoreCase);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpClient CreateClient(
        FakeLogger<SupplierLoggingHandler> logger,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new SupplierLoggingHandler("TestSupplier", SensitiveFields, logger)
        {
            InnerHandler = new StubHttpMessageHandler(respond)
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://supplier.test") };
    }

    [Fact]
    public async Task Redaction_removes_password_from_logged_token_request()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(Json("{}")));

        using var response = await client.PostAsJsonAsync("/token", new { username = "flamingo", password = "secret" });

        var message = logger.LatestRecord.Message;
        Assert.Contains("flamingo", message);
        Assert.Contains("***", message);
        Assert.DoesNotContain("secret", message);
        Assert.Contains("-> 200", message);
        Assert.Contains(" ms.", message);
    }

    [Fact]
    public async Task Redaction_removes_access_token_from_logged_token_response()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(Json("""{"access_token":"tok-123","expires_in":600}""")));

        using var response = await client.PostAsJsonAsync("/token", new { username = "flamingo" });

        var message = logger.LatestRecord.Message;
        Assert.Contains("access_token", message);
        Assert.Contains("***", message);
        Assert.DoesNotContain("tok-123", message);
    }

    [Fact]
    public async Task Redaction_removes_nested_sign_but_keeps_partnerId()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(Json("{}")));

        using var response = await client.PostAsJsonAsync("/order", new { auth = new { partnerId = "flamingo", sign = "abc123" } });

        var message = logger.LatestRecord.Message;
        Assert.Contains("flamingo", message);
        Assert.DoesNotContain("abc123", message);
    }

    [Fact]
    public async Task Logging_never_writes_the_Authorization_header_value()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(Json("{}")));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/search")
        {
            Content = JsonContent.Create(new { origin = "BGW" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tok-123");

        using var response = await client.SendAsync(request);

        var message = logger.LatestRecord.Message;
        Assert.Contains("BGW", message);
        Assert.All(logger.Collector.GetSnapshot(), record => Assert.DoesNotContain("tok-123", record.Message));
    }

    [Fact]
    public async Task Logging_leaves_the_response_body_readable_for_the_caller()
    {
        const string tokenResponse = """{"access_token":"tok-123","expires_in":600}""";
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(Json(tokenResponse)));

        using var response = await client.PostAsJsonAsync("/token", new { username = "flamingo" });
        var body = await response.Content.ReadAsStringAsync();

        var message = logger.LatestRecord.Message;
        Assert.Equal(tokenResponse, body);
        Assert.DoesNotContain("tok-123", message);
    }

    [Fact]
    public async Task Logging_logs_a_warning_and_rethrows_when_the_call_times_out()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json("{}");
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsJsonAsync("/book", new { offer_token = "aeh_91c2d4" }, timeout.Token));

        Assert.Equal(LogLevel.Warning, logger.LatestRecord.Level);
        Assert.IsAssignableFrom<OperationCanceledException>(logger.LatestRecord.Exception);
    }

    [Fact]
    public async Task Logging_does_not_print_a_non_JSON_body()
    {
        var logger = new FakeLogger<SupplierLoggingHandler>();
        using var client = CreateClient(logger, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>password=secret</html>", Encoding.UTF8, "text/html")
        }));

        using var response = await client.GetAsync("/order/917000000001");

        var message = logger.LatestRecord.Message;
        Assert.DoesNotContain("secret", message);
        Assert.Contains("non-JSON", message);
        Assert.Contains("-> 502", message);
        Assert.Contains("(none)", message);
    }
}