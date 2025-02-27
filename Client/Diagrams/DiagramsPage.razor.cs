using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private Diagram[]? _diagrams;

    [Inject] private UserManager UserManager { get; set; } = null!;
    [Inject] private HttpClient Client { get; set; } = null!;

    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private DiagramManager DiagramManager { get; set; } = null!;
    
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = null!;

    private ILogger _logger = null!;
    
    protected override async Task OnInitializedAsync()
    {
        await UserManager.Update();
        await GetAllDiagrams();
    }

    protected override void OnParametersSet()
    {
        _logger = LoggerFactory.CreateLogger<DiagramsPage>();
    }

    private async Task GetAllDiagrams()
    {
        try
        {
            _diagrams = await Client.GetFromJsonAsync<Diagram[]>(ApplicationApi.Diagrams.Get.Request) ?? [];
        }
        catch (Exception e)
        {
            // TODO: Remove - security risk
            _logger.LogError(e, "Fetching diagram failed");
            var response = await Client.GetAsync(ApplicationApi.Diagrams.Get.Request);
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogError("Fetching diagrams failed with response: {Content}", content);
            _diagrams = [];
        }
    }

    private async Task OnAddNewTrendDiagram()
    {
        const string namePrefix = "New Trends Diagram";
        var matchingDiagramCount = _diagrams!.Count(d => d.Name.StartsWith(namePrefix)) + 1;
        
        var diagram = new Diagram
        {
            Owner = UserManager.CurrentUser,
            Name = $"{namePrefix} {matchingDiagramCount}",
            Description = "Provide a short description",
            Id = DiagramIdentifier.NewIdentifier(),
            Position = new DiagramPosition { X = 0, Y = 0 },
            CreationDate = DateTime.UtcNow,
            ModificationDate = DateTime.UtcNow,
            Zoom = 0,
        };
        var response = await Client.PostAsJsonAsync(ApplicationApi.Diagrams.Add.Request, diagram);
        diagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        _diagrams = _diagrams!.Concat([diagram]).ToArray();
        StateHasChanged();
    }
    
    private async Task EditedDiagram(Diagram diagram)
    {
        var response = await Client.PutAsJsonAsync(ApplicationApi.Diagrams.Edit.Request(diagram.Id), diagram);
        var changedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        var diagramIndex = _diagrams!.Index().Single(d => d.Item.Id == changedDiagram.Id).Index;
        _diagrams![diagramIndex] = changedDiagram;
        StateHasChanged();
    }
    
    private async Task DeleteDiagram(Diagram diagram)
    {
        var response = await Client.DeleteAsync(ApplicationApi.Diagrams.Remove.Request(diagram.Id));
        var removedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        _diagrams = _diagrams!
            .Where(d => d.Id != removedDiagram.Id)
            .ToArray();
        StateHasChanged();
    }

    private void ViewDiagram(Diagram diagram)
    {
        DiagramManager.SetCurrentDiagram(diagram);
        NavigationManager.NavigateTo($"/user/diagrams/{diagram.Id.Identifier}");
        StateHasChanged();
    }
}