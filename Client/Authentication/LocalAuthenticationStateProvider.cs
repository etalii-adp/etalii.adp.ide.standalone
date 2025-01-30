using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public class LocalAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new GenericIdentity("test-user");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "83b5582c-2f9e-49df-87ff-d5792440af2e"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "test-user"));
        //identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

        var state = new AuthenticationState(new ClaimsPrincipal(identity));
        return Task.FromResult(state);
    }
}