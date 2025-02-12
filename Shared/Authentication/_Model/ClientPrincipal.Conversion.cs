using System.Security.Claims;
using System.Security.Principal;

namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    public static ClaimsPrincipal ToClaimsPrincipal(ClientPrincipal? clientPrincipal)
    {
        if (clientPrincipal is null || !clientPrincipal.UserRoles.Any() || clientPrincipal.UserId is null || clientPrincipal.UserDetails is null)
        {
            return new ClaimsPrincipal();
        }

        try
        {
            clientPrincipal.UserRoles = clientPrincipal.UserRoles
                .Except(["anonymous"], StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (!clientPrincipal.UserRoles.Any())
            {
                return new ClaimsPrincipal();
            }

            var identity = new ClaimsIdentity(clientPrincipal.IdentityProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, clientPrincipal.UserId!));
            identity.AddClaim(new Claim(ClaimTypes.Name, clientPrincipal.UserDetails!));
            identity.AddClaim(new Claim(ClaimTypes.Sid, clientPrincipal.ExternalIdentifier));
            identity.AddClaims(clientPrincipal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return new ClaimsPrincipal();
        }
    }
    
    public static ClientPrincipal ToClientPrincipal(IIdentity identity) => ToClientPrincipal((ClaimsIdentity)identity);

    private static ClientPrincipal ToClientPrincipal(ClaimsIdentity claimsIdentity)
    {
        if (!claimsIdentity.Claims.Any())
        {
            return null!;
        }
        
        return new ClientPrincipal
        {
            IdentityProvider = claimsIdentity.AuthenticationType,
            UserRoles = claimsIdentity.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray(),
            UserId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)!.Value,
            UserDetails = claimsIdentity.FindFirst(ClaimTypes.Name)!.Value,
            ExternalIdentifier = claimsIdentity.FindFirst(ClaimTypes.Sid)!.Value,
        };
    }
}