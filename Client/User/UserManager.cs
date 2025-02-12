using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public class UserManager
{
    public User CurrentUser { get; private set; } = null!;
    public ClaimsPrincipal ClaimsPrincipal { get; private set; } = null!;

    private ClientPrincipal _clientPrincipal = null!;
    public bool IsAuthenticated => _clientPrincipal != null! && (ClaimsPrincipal.Identity?.IsAuthenticated ?? false);
        
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    private readonly HttpClient _client;

    public UserManager(
        AuthenticationStateProvider authenticationStateProvider,
        HttpClient client)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _client = client;
    }

    public async Task Update()
    {
        if (CurrentUser == null! || _clientPrincipal == null!)
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal = authState.User;

            _clientPrincipal = ClientPrincipal.ToClientPrincipal(ClaimsPrincipal.Identity!);

            if (IsAuthenticated)
            {
#if DEBUG                
                ClientPrincipal.SetHeader(_client.DefaultRequestHeaders, ClaimsPrincipal);
#endif
                CurrentUser = await _client.GetFromJsonAsync<User>(ApplicationApi.Authentication.Get.Request) ?? null!;
            }
        }
    }

    public async Task LoginLocalTestUser(User user)
    {
        ((LocalAuthenticationStateProvider)_authenticationStateProvider).MarkUserAsAuthenticated(user);
        await Update();
    }

    public async Task LogoutLocalTestUser()
    {
        ((LocalAuthenticationStateProvider)_authenticationStateProvider).MarkUserAsLoggedOut();
        ClaimsPrincipal = null!;
        _clientPrincipal = null!;
        await Update();
    }
}