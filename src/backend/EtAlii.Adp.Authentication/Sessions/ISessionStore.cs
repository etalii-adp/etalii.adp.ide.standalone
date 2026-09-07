namespace EtAlii.Adp.Authentication;

public interface ISessionStore
{
    string Issue(ShortGuid userId);

    bool TryValidate(string token, out ShortGuid userId);

    void Revoke(string token);
}
