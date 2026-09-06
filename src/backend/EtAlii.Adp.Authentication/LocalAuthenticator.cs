using Microsoft.Extensions.Options;

namespace EtAlii.Adp.Authentication;

/// <summary>
/// Default authenticator for the local, standalone "F5" scenario: validates
/// against a single credential from configuration, requiring no external
/// identity provider (Requirement 1.4).
/// </summary>
public sealed class LocalAuthenticator : IAuthenticator
{
    private readonly LocalAuthenticatorOptions _options;

    public LocalAuthenticator(IOptions<LocalAuthenticatorOptions> options)
    {
        _options = options.Value;
    }

    public bool Validate(string username, string credential) =>
        username == _options.Username && credential == _options.Credential;
}
