namespace EtAlii.Adp.Backend.Authentication;

public interface IAuthenticator
{
    bool Validate(string username, string credential);
}
