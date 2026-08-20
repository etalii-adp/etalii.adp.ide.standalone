namespace EtAlii.Adp.Backend.Sessions;

public interface ISessionStore
{
    string Issue(string username);

    bool TryValidate(string token, out string? username);

    void Revoke(string token);
}
