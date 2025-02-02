using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private async Task LoginLocalDeveloper()
    {
        var username = "Mock User"; // You can replace this with any logic to simulate different users
        ((LocalAuthenticationStateProvider)AuthenticationStateProvider).MarkUserAsAuthenticated(username);
        Navigation.NavigateTo(_landingUrlAfterLogin);
        //await UpdateBreadcrumbs();
    }

    private void LogoutLocalDeveloper()
    {
        ((LocalAuthenticationStateProvider)AuthenticationStateProvider).MarkUserAsLoggedOut();
        // username = null;
        Navigation.NavigateTo(_landingUrlAfterLogout);
        //Navigation.Refresh();
    }
}