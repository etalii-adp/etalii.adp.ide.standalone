using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public class LocalAuthenticationStateProvider : AuthenticationStateProvider
{
    private AuthenticationState _currentUserState;

    public LocalAuthenticationStateProvider()
    {
        _currentUserState = CreateEmptyState();
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
#if DEBUG        
        return Task.FromResult(_currentUserState);
#else
        return Task.FromResult(null);
#endif
    }

    public void MarkUserAsAuthenticated(string username)
    {
#if DEBUG        
        var identity = new GenericIdentity("test-user");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "83b5582c-2f9e-49df-87ff-d5792440af2e"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "test-user"));
        //identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

        _currentUserState = new AuthenticationState(new ClaimsPrincipal(identity));
        
        NotifyAuthenticationStateChanged(Task.FromResult(_currentUserState));
#endif        
    }

    public void MarkUserAsLoggedOut()
    {
#if DEBUG
        _currentUserState = CreateEmptyState();

        NotifyAuthenticationStateChanged(Task.FromResult(_currentUserState));
#endif        
    }

    private AuthenticationState CreateEmptyState()
    {
        var identity = new ClaimsIdentity();
        var user = new ClaimsPrincipal(identity);
        return new AuthenticationState(user);
    }
}