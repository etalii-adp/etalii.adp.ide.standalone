using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private SelectedPage _selectedPage = SelectedPage.Adp;

    private MarkupString _ubigiaLink;
    private MarkupString _adpLink;
    private MarkupString _userLink;
    private MarkupString _diagramsLink;
    private MarkupString _diagramLink;
    private string? _userName;
    private const string _relativeLandingPageAfterLogin = "/user/diagrams";
    private const string _relativeLandingPageAfterLogout = "/";
    private const string _cloudHostName = "ubigia.net";

    private readonly string _landingUrlAfterLogin = LocalDebugger.IsAttached
        ? _relativeLandingPageAfterLogin
        : new Uri($"https://{_cloudHostName}{_relativeLandingPageAfterLogin}").ToString();

    private readonly string _landingUrlAfterLogout = LocalDebugger.IsAttached
        ? _relativeLandingPageAfterLogout
        : new Uri($"https://{_cloudHostName}{_relativeLandingPageAfterLogout}").ToString();
    
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    [Inject] private UserManager UserManager { get; set; } = null!;
    
    private bool _isLoaded;
    
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
        var currentPath = Navigation
            .ToBaseRelativePath(Navigation.Uri)
            .ToLower();

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
        else if (currentPath.EndsWith("ubigia"))
        {
            _selectedPage = SelectedPage.Ubigia;
        }
        else
        {
            _selectedPage = SelectedPage.Adp;
        }

        await UserManager.Update();
        if (UserManager.IsAuthenticated)
        {
            _userName = UserManager.ClaimsPrincipal.Identity?.Name ?? null!;
        }
        else
        {
            _userName = null;
        }

        _ubigiaLink = _selectedPage == SelectedPage.Ubigia
            ? new MarkupString($"<b><a style=\"color:black\" href=\"/ubigia\">Ubigia</a></b> / ")
            : new MarkupString($"<a href=\"/ubigia\">Ubigia</a> / ");

        _adpLink = _selectedPage == SelectedPage.Adp
            ? new MarkupString($"<b><a style=\"color:black\" href=\"/\">Adp</a></b>{(_userName != null ? " / " : "")}")
            : new MarkupString($"<a href=\"/\">Adp</a>{(_userName != null ? " / " : "")}");

        _userLink = _selectedPage == SelectedPage.User
            ? new MarkupString($"<b><a style=\"color:black\" href=\"/user\">{_userName}</a></b> /")
            : new MarkupString($"<a href=\"/user\">{_userName}</a> /");

        _diagramsLink = _selectedPage == SelectedPage.Diagrams
            ? new MarkupString($"<b><a style=\"color:black\" href=\"/user/diagrams\">Diagrams</a></b>")
            : new MarkupString($"<a href=\"/user/diagrams\">Diagrams</a>{(_selectedPage == SelectedPage.Diagram ? " / " : "")}");

        if (DiagramManager.CurrentDiagram != null!)
        {
            _diagramLink = _selectedPage == SelectedPage.Diagram
                ? new MarkupString($"<b><a style=\"color:black\" href=\"/user/diagrams/{DiagramManager.CurrentDiagram.Id}\">{DiagramManager.CurrentDiagram.Name}</a></b>")
                : new MarkupString($"<a href=\"/user/diagrams/{DiagramManager.CurrentDiagram.Id}\">{DiagramManager.CurrentDiagram.Name}</a>");
        }
        else
        {
            _diagramLink = new MarkupString();
        }

        _isLoaded = true;
        StateHasChanged();
    }
}