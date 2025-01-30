using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace EtAlii.Adp.Client;

public class CloudAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _client;

    public CloudAuthenticationStateProvider(IWebAssemblyHostEnvironment environment)
    {
        _client = new HttpClient { BaseAddress = new Uri(environment.BaseAddress) };
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try 
        {
            var state = await _client.GetFromJsonAsync<UserAuthenticationState>("/.auth/me");

            var principal = state!.ClientPrincipal;
            principal.UserRoles = principal.UserRoles
                .Except(["anonymous"], StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (!principal.UserRoles.Any())
            {
                return new AuthenticationState(new ClaimsPrincipal());
            }

            var identity = new ClaimsIdentity(principal.IdentityProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.UserId!));
            identity.AddClaim(new Claim(ClaimTypes.Name, principal.UserDetails!));
            identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

            return new AuthenticationState(new ClaimsPrincipal(identity));
        } 
        catch(Exception) 
        {
            return new AuthenticationState(new ClaimsPrincipal());
        }
    }
}