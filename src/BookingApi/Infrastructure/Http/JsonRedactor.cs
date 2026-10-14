using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookingApi.Infrastructure.Http;

public static class JsonRedactor
{
    private const string Mask = "***";

    public static string? Redact(string body, IReadOnlySet<string> sensitiveFields)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
        RedactNode(node, sensitiveFields);
        return node?.ToJsonString() ?? body;
    }

    private static void RedactNode(JsonNode? node, IReadOnlySet<string> sensitiveFields)
    {
        if (node is JsonObject jsonObject)
        {
            var keys = jsonObject.Select(property => property.Key).ToList();
            foreach (var key in keys)
            {
                if (sensitiveFields.Contains(key))
                {
                    jsonObject[key] = Mask;
                }
                else
                {
                    RedactNode(jsonObject[key], sensitiveFields);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                RedactNode(element, sensitiveFields);
            }
        }
    }
}
