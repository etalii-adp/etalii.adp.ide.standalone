using System.Security.Claims;

namespace EtAlii.Adp;

public static class AuthenticationHelper
{
    public static ClaimsPrincipal GetClaimsPrincipalFromClientPrincipal(ClientPrincipal? clientPrincipal)
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
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, clientPrincipal.UserId));
            identity.AddClaim(new Claim(ClaimTypes.Name, clientPrincipal.UserDetails));
            identity.AddClaims(clientPrincipal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return new ClaimsPrincipal();
        }
    }
}