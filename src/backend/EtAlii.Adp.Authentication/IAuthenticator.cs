namespace EtAlii.Adp.Authentication;

public interface IAuthenticator
{
    bool Validate(string username, string credential);
}
