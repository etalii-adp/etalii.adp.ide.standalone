using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private string? _lastDiagram = "1234";
    private SelectedPage _selectedPage = SelectedPage.Home;
    private string? _userName = null;//"vrenken@live.nl";

    private MarkupString _ubigiaLink;
    private MarkupString _userLink;
    private MarkupString _diagramsLink;
    private MarkupString _diagramLink;
    
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
        
        if (AuthenticationStateProvider != null!)
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            _userName = user.Identity is { } identity 
                ? identity.Name 
                : null;
        }
        else
        {
            _userName = null;
        }

        if (_userName != null)
        {
            
        }
        else
        {
            _lastDiagram = null;
        }

        _userName = "Test User";
        _lastDiagram = "Test Diagram";

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