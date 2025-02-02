using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private void LoginLocalTestUser(LocalTestUser user)
    {
        ((LocalAuthenticationStateProvider)AuthenticationStateProvider).MarkUserAsAuthenticated(user);
        Navigation.NavigateTo(_landingUrlAfterLogin);
    }

    private void LogoutLocalTestUser()
    {
        ((LocalAuthenticationStateProvider)AuthenticationStateProvider).MarkUserAsLoggedOut();
        Navigation.NavigateTo(_landingUrlAfterLogout);
    }
}