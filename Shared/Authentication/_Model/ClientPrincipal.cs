namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    public required string? IdentityProvider { get; init; } = string.Empty;
    public required string? UserId { get; init; } = string.Empty;
    public required string? UserDetails { get; init; } = string.Empty;
    public required string[] UserRoles { get; set; } = [];
    public required string? ExternalIdentifier { get; set; } = string.Empty;
}