using System.Collections.Concurrent;

namespace EtAlii.Adp.Backend.Sessions;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, string> _sessionsByToken = new();

    public string Issue(string username)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessionsByToken[token] = username;
        return token;
    }

    public bool TryValidate(string token, out string? username) =>
        _sessionsByToken.TryGetValue(token, out username);

    public void Revoke(string token) =>
        _sessionsByToken.TryRemove(token, out _);
}
