using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EtAlii.Adp.Client;

public partial class HomePage
{
    [Inject] private HttpClient Http {get; set;} = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider {get; set;} = null!;

    private ClaimsPrincipal? _user;

    private bool _isUserAuthenticated;

    private bool _isUserAdmin;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _user = authState.User;
        _isUserAuthenticated = _user?.Identity?.IsAuthenticated ?? false;
        _isUserAdmin = _user?.IsInRole("admin") ?? false;
    }

    private string? _publicApiResponse;
    private string? _protectedApiResponse;
    private string? _protectedAdminApiResponse;
    private string? _protectedSuperAdminApiResponse;

    private string _superAdminFunctionMessageColor = "green";
    private string _adminFunctionMessageColor = "green";

    private async Task CallPublicFunction()
    {
        _publicApiResponse = await Http.GetStringAsync("/api/hello");
    }

    private async Task CallProtectedFunction()
    {
        _protectedApiResponse = await Http.GetStringAsync("/api/hello/protected");
    }

    private async Task CallProtectedAdminFunction()
    {
        var response = await Http.GetAsync("/api/hello/protected/admin");
        _protectedAdminApiResponse = await response.Content.ReadAsStringAsync();
        _adminFunctionMessageColor = response.IsSuccessStatusCode ? "green" : "red";
    }


    private async Task CallProtectedSuperAdminFunction()
    {
        var response = await Http.GetAsync("/api/hello/protected/superadmin");
        _protectedSuperAdminApiResponse = await response.Content.ReadAsStringAsync();

        _superAdminFunctionMessageColor = response.IsSuccessStatusCode ? "green" : "red";
    }
}