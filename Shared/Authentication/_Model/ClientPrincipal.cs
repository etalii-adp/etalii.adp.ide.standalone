namespace EtAlii.Adp;

public class ClientPrincipal
{
    public string? IdentityProvider { get; set; }
    public string? UserId { get; set; }
    public string? UserDetails { get; set; }
    public string[] UserRoles { get; set; } = [];
    // public ClientPrincipalClaim[] Claims { get; set; } = [];
    //
    // public ClaimsIdentity ToClaimsIdentity()
    // {
    //     var claimsIdentity = new ClaimsIdentity(IdentityProvider);
    //     claimsIdentity = new ClaimsIdentity(IdentityProvider);
    //     claimsIdentity.AddClaim(new Claim(ClaimTypes.Name, UserDetails));
    //     claimsIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, UserId));
    //     claimsIdentity.AddClaims(UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));
    //     foreach (var claim in Claims)
    //     {
    //         claimsIdentity.AddClaim(new Claim(claim.Typ, claim.Val));
    //     }
    //     return claimsIdentity;
    // }
}