namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    public string? IdentityProvider { get; init; } = string.Empty;
    public string? UserId { get; init; } = string.Empty;
    public string? UserDetails { get; init; } = string.Empty;
    public string[] UserRoles { get; set; } = [];
}