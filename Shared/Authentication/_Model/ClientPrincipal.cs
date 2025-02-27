namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    public string? IdentityProvider { get; set; } = string.Empty;
    public string? UserId { get; set; } = string.Empty;
    public string? UserDetails { get; set; } = string.Empty;
    public string[] UserRoles { get; set; } = [];
    public string? ExternalIdentifier { get; set; } = string.Empty;
}