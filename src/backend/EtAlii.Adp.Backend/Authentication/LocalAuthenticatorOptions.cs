namespace EtAlii.Adp.Backend.Authentication;

public sealed class LocalAuthenticatorOptions
{
    public const string SectionName = "LocalAuthenticator";

    public string Username { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
}
