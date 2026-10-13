using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AeroHubMock;

public sealed class TokenStore(TimeSpan lifetime)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();

    public TimeSpan Lifetime => lifetime;

    public string Issue()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _tokens[token] = DateTimeOffset.UtcNow.Add(lifetime);
        return token;
    }

    public bool IsValid(string token)
    {
        if (!_tokens.TryGetValue(token, out var expiresAt))
        {
            return false;
        }

        if (expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _tokens.TryRemove(token, out _);
        return false;
    }
}
