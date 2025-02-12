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
        return Task.FromResult<AuthenticationState>(null!);
#endif
    }

    public void MarkUserAsAuthenticated(User user)
    {
#if DEBUG // Only for testing locally
        var identity = new GenericIdentity(user.Name);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.Identifier.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Sid, user.ExternalIdentifier));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Name));
        identity.AddClaim(new Claim(ClaimTypes.Role, "authenticated")); 
        // identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

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