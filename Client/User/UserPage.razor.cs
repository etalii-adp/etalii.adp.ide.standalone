using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public partial class UserPage
{
    [Inject] public AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    [Inject] private HttpClient Http {get; set;} = null!;

    private ClaimsPrincipal? _user;
    
    private bool IsUserAuthenticated => _user?.Identity?.IsAuthenticated ?? false;
    private bool IsUserAdmin => _user?.IsInRole("admin") ?? false;

    private string? _publicApiResponse;
    private string? _protectedApiResponse;
    private string? _protectedAdminApiResponse;
    private string? _protectedSuperAdminApiResponse;

    private string _superAdminFunctionMessageColor = "green";
    private string _adminFunctionMessageColor = "green";

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _user = state.User;
    }
    
    private async Task CallPublicFunction()
    {
        _publicApiResponse = await Http.GetStringAsync("/api/hello");
    }

    private async Task CallProtectedFunction()
    {
        _protectedApiResponse = await Http.GetStringAsync("/api/hello_protected");
    }

    private async Task CallProtectedAdminFunction()
    {
        var response = await Http.GetAsync("/api/hello_protected_admin");
        _protectedAdminApiResponse = await response.Content.ReadAsStringAsync();
        _adminFunctionMessageColor = response.IsSuccessStatusCode ? "green" : "red";
    }


    private async Task CallProtectedSuperAdminFunction()
    {
        var response = await Http.GetAsync("/api/hello_protected_superadmin");
        _protectedSuperAdminApiResponse = await response.Content.ReadAsStringAsync();

        _superAdminFunctionMessageColor = response.IsSuccessStatusCode ? "green" : "red";
    }
}