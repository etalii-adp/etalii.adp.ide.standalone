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

            var claimsPrincipal = ClientPrincipal.ToClaimsPrincipal(principal);
            return new AuthenticationState(claimsPrincipal);
        } 
        catch(Exception) 
        {
            return new AuthenticationState(new ClaimsPrincipal());
        }
    }
}