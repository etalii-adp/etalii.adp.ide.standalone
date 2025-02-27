using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public partial class UserManager
{
    public User CurrentUser { get; private set; } = null!;
    public ClaimsPrincipal ClaimsPrincipal { get; private set; } = null!;

    private ClientPrincipal _clientPrincipal = null!;
    public bool IsAuthenticated => _clientPrincipal != null! && (ClaimsPrincipal.Identity?.IsAuthenticated ?? false);
        
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    private readonly HttpClient _client;

    private readonly ILogger _logger;
    public UserManager(
        AuthenticationStateProvider authenticationStateProvider,
        HttpClient client,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<UserManager>();
        _authenticationStateProvider = authenticationStateProvider;
        _client = client;
    }

    public async Task Update()
    {
        if (CurrentUser == null! || _clientPrincipal == null!)
        {
            await WaitUntilBackedIsResponsive();

            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            ClaimsPrincipal = authState.User;

            _clientPrincipal = ClientPrincipal.ToClientPrincipal(ClaimsPrincipal.Identity!, _logger);

            if (IsAuthenticated)
            {
                ClientPrincipal.SetHeader(_client, ClaimsPrincipal, _logger);
                
                var response = await _client.GetAsync(ApplicationApi.Authentication.Get.Request);
                if (response.IsSuccessStatusCode)
                {
                    var user = await response.Content.ReadFromJsonAsync<User>();
                    CurrentUser = user!;
                }
                else
                {
                    // TODO: Remove - security risk
                    if (response.Headers.TryGetValues("Diagnostics", out var values))
                    {
                        var diagnostics = string.Join(',', values);
                        _logger.LogError("Unable to authenticating user: {Diagnostics}", diagnostics);
                    }
                    _clientPrincipal = null!;
                }
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