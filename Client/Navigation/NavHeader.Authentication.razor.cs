using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private Modal _loginModal = null!;

    private async Task ShowLoginModal()
    {
        await _loginModal.ShowAsync();
    }

    private void LoginUsingGitHub()
    {
        Navigation.NavigateTo($"/.auth/login/github?post_login_redirect_uri=/login/{Base64Url.Encode(_landingUrlAfterLogin)}", true);
    }

    private void LoginUsingMicrosoft()
    {
        Navigation.NavigateTo($"/.auth/login/aad?post_login_redirect_uri=/login/{Base64Url.Encode(_landingUrlAfterLogin)}", true);
    }
    
    private async Task LoginLocalTestUser(User user)
    {
        await UserManager.LoginLocalTestUser(user);
        Navigation.NavigateTo($"/login/{Base64Url.Encode(_landingUrlAfterLogin)}");
    }

    private async Task LogoutLocalTestUser()
    {
        await UserManager.LogoutLocalTestUser();
        Navigation.NavigateTo(_landingUrlAfterLogout);
    }
}