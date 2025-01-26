using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client.Pages;

public partial class User
{
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ClaimsPrincipal? _authenticatedUser;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _authenticatedUser = state.User;
    }
}