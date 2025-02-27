using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace EtAlii.Adp.Client;

public class CloudAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;

    // private static readonly string[] AuthenticatedRole = ["authenticated"];
    private static readonly string[] AnonymousRole = ["anonymous"];
    
    public CloudAuthenticationStateProvider(IWebAssemblyHostEnvironment environment, ILoggerFactory loggerFactory)
    {
        _client = new HttpClient { BaseAddress = new Uri(environment.BaseAddress) };
        _logger = loggerFactory.CreateLogger<CloudAuthenticationStateProvider>();
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var state = await _client.GetFromJsonAsync<UserAuthenticationState>("/.auth/me");

            var principal = state!.ClientPrincipal;
            principal.UserRoles = principal.UserRoles
                .Except(AnonymousRole, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            // if (!string.IsNullOrEmpty(principal.UserId))
            // {
            //     principal.UserRoles = principal.UserRoles
            //         .Except(AuthenticatedRole, StringComparer.CurrentCultureIgnoreCase)
            //         .Concat(AuthenticatedRole)
            //         .ToArray();
            // }
            if (string.IsNullOrEmpty(principal.ExternalIdentifier))
            {
                principal.ExternalIdentifier = $"{principal.UserId}@{principal.IdentityProvider}";
            }
            var claimsPrincipal = ClientPrincipal.ToClaimsPrincipal(principal, _logger);
            _logger.LogInformation("Authentication state retrieved successfully for {UserName}", principal.UserDetails);
            return new AuthenticationState(claimsPrincipal);
        } 
        catch(Exception e) 
        {
            _logger.LogError(e, "Authentication state retrieval failed");
            return new AuthenticationState(new ClaimsPrincipal());
        }
    }
}