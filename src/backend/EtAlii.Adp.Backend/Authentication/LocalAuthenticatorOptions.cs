namespace EtAlii.Adp.Backend.Authentication;

public sealed class LocalAuthenticatorOptions
{
    public const string SectionName = "LocalAuthenticator";

    public string Username { get; init; } = string.Empty;
    public string Credential { get; init; } = string.Empty;
}
