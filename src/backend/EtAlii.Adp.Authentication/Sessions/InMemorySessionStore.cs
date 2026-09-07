using System.Collections.Concurrent;

namespace EtAlii.Adp.Authentication;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, ShortGuid> _sessionsByToken = new();

    public string Issue(ShortGuid userId)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessionsByToken[token] = userId;
        return token;
    }

    public bool TryValidate(string token, out ShortGuid userId) =>
        _sessionsByToken.TryGetValue(token, out userId);

    public void Revoke(string token) =>
        _sessionsByToken.TryRemove(token, out _);
}
