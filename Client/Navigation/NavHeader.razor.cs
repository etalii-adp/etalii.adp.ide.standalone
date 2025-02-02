using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private string? _lastDiagram = "1234";
    private SelectedPage _selectedPage = SelectedPage.Home;

    private MarkupString _ubigiaLink;
    private MarkupString _userLink;
    private MarkupString _diagramsLink;
    private MarkupString _diagramLink;
    private string? _userName;
    private const string RelativeLandingPageAfterLogin = "/user/diagrams";
    private const string RelativeLandingPageAfterLogout = "/";
    private const string CloudHostName = "www.ubigia.net";

    private readonly string _landingUrlAfterLogin = LocalDebugger.IsAttached
        ? RelativeLandingPageAfterLogin
        : new Uri($"https://{CloudHostName}{RelativeLandingPageAfterLogin}").ToString();

    private readonly string _landingUrlAfterLogout = LocalDebugger.IsAttached
        ? RelativeLandingPageAfterLogout
        : new Uri($"https://{CloudHostName}{RelativeLandingPageAfterLogout}").ToString();

    
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    
    protected override async Task OnInitializedAsync()
    {
        await UpdateBreadcrumbs();
        
        // Subscribe to the LocationChanged event
        Navigation.LocationChanged += OnLocationChanged;
    }

    
    public void Dispose()
    {
        // Unsubscribe from the LocationChanged event to avoid memory leaks
        Navigation.LocationChanged -= OnLocationChanged;
    }
    
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => InvokeAsync(UpdateBreadcrumbs);

    private async Task UpdateBreadcrumbs()
    {
        // Get the current URI and extract the path
        var currentPath = Navigation.ToBaseRelativePath(Navigation.Uri);

        if (currentPath.Contains("user/diagrams/"))
        {
            _selectedPage = SelectedPage.Diagram;
        }
        else if (currentPath.Contains("user/diagrams"))
        {
            _selectedPage = SelectedPage.Diagrams;
        }
        else if (currentPath.Contains("user"))
        {
            _selectedPage = SelectedPage.User;
        }
        else
        {
            _selectedPage = SelectedPage.Home;
        }
        
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var isUserAuthenticated = user.Identity?.IsAuthenticated ?? false;
        
        if (isUserAuthenticated)
        {
            _userName = user.Identity?.Name ?? null!;
            _lastDiagram = "Test Diagram";
        }
        else
        {
            _userName = null;
            _lastDiagram = null;
        }

        _ubigiaLink = _selectedPage == SelectedPage.Home
            ? new MarkupString($"<b><a href=\"/\">Ubigia</a></b>{(_userName != null ? " / " : "")}")
            : new MarkupString($"<a href=\"/\">Ubigia</a>{(_userName != null ? " / " : "")}");
        
        _userLink = _selectedPage == SelectedPage.User
            ? new MarkupString($"<b><a href=\"/user\">{_userName}</a></b> /")
            : new MarkupString($"<a href=\"/user\">{_userName}</a> /");

        _diagramsLink = _selectedPage == SelectedPage.Diagrams
            ? new MarkupString($"<b><a href=\"/user/diagrams\">Diagrams</a></b>{(_lastDiagram != null ? " / " : "")}")
            : new MarkupString($"<a href=\"/user/diagrams\">Diagrams</a>{(_lastDiagram != null ? " / " : "")}");

        if (_lastDiagram != null)
        {
            _diagramLink = _selectedPage == SelectedPage.Diagram
                ? new MarkupString($"<b><a href=\"/user/diagrams/{_lastDiagram}\">{_lastDiagram}</a></b>")
                : new MarkupString($"<a href=\"/user/diagrams/{_lastDiagram}\">{_lastDiagram}</a>");
        }
        else
        {
            _diagramLink = new MarkupString();
        }
        StateHasChanged();
    }
}