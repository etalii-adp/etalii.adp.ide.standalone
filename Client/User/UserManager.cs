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
                try
                {
                    ClientPrincipal.SetHeader(_client, ClaimsPrincipal, _logger);
                    CurrentUser = await _client.GetFromJsonAsync<User>(ApplicationApi.Authentication.Get.Request) ?? null!;
                }
                catch (Exception e)
                {
                    // TODO: Remove - security risk
                    _logger.LogError(e, "Unable authenticating user");
                    var response = await _client.GetAsync(ApplicationApi.Authentication.Get.Request);
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Unable authenticating user: {Content}", content);
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