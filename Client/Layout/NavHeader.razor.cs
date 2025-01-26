using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace EtAlii.Adp.Client;

public partial class NavHeader
{
    private string? _lastDiagram = "1234";
    private SelectedPage _selectedPage = SelectedPage.Home;
    private string? _userName = "vrenken@live.nl";

    private MarkupString _ubigiaLink;
    private MarkupString _userLink;
    private MarkupString _diagramsLink;
    private MarkupString _diagramLink;
    
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    
    protected override void OnInitialized()
    {
        UpdateBreadcrumbs();
        
        // Subscribe to the LocationChanged event
        Navigation.LocationChanged += OnLocationChanged;
    }

    
    public void Dispose()
    {
        // Unsubscribe from the LocationChanged event to avoid memory leaks
        Navigation.LocationChanged -= OnLocationChanged;
    }
    
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => UpdateBreadcrumbs();

    private void UpdateBreadcrumbs()
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


        _ubigiaLink = _selectedPage == SelectedPage.Home
            ? new MarkupString("<b><a href=\"/\">Ubigia</a></b> /")
            : new MarkupString("<a href=\"/\">Ubigia</a> /");
        
        _userLink = _selectedPage == SelectedPage.User
            ? new MarkupString($"<b><a href=\"/user\">{_userName}</a></b> /")
            : new MarkupString($"<a href=\"/user\">{_userName}</a> /");

        if (_lastDiagram != null)
        {
            _diagramsLink = _selectedPage == SelectedPage.Diagrams
                ? new MarkupString("<b><a href=\"/user/diagrams\">Diagrams</a></b> /")
                : new MarkupString("<a href=\"/user/diagrams\">Diagrams</a> /");
        }
        else
        {
            _diagramsLink = _selectedPage == SelectedPage.Diagrams
                ? new MarkupString("<b><a href=\"/user/diagrams\">Diagrams</a></b>")
                : new MarkupString("<a href=\"/user/diagrams\">Diagrams</a>");
        }

        if (_lastDiagram != null)
        {
            _diagramLink = _selectedPage == SelectedPage.Diagram
                ? new MarkupString($"<b><a href=\"/user/diagrams/{_lastDiagram}\">{_lastDiagram}</a></b> /")
                : new MarkupString($"<a href=\"/user/diagrams/{_lastDiagram}\">{_lastDiagram}</a> /");
        }
        StateHasChanged();
    }
}