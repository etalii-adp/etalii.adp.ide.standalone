using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private Modal _loginModal = null!;

    private async Task ShowLoginModal()
    {
        await _loginModal.ShowAsync();
    }

    private void LoginUsingGitHub()
    {
        Navigation.NavigateTo($"/.auth/login/github?post_login_redirect_uri={_landingUrlAfterLogin}", true);
    }

    private void LoginUsingMicrosoft()
    {
        Navigation.NavigateTo($"/.auth/login/aad?post_login_redirect_uri={_landingUrlAfterLogin}", true);
    }
}