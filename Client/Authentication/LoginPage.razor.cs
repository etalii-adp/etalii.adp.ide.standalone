using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class LoginPage : ComponentBase
{
    [Parameter] public string TargetPage { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    
    [Inject] private UserManager UserManager { get; set; } = null!;
    
    protected override async Task OnInitializedAsync()
    {
        // Initialize.
        await UserManager.Update();

        // Store.

        // Redirect.
        var targetPage = Base64Url.Decode(TargetPage);
        Navigation.NavigateTo(targetPage);
    }
}