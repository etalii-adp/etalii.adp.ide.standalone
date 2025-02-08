using System.Net.Http.Json;
using System.Numerics;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private Diagram[]? _diagrams;

    protected override async Task OnInitializedAsync()
    {
        await GetAllDiagrams();
    }

    private async Task GetAllDiagrams()
    {
        _diagrams = await Http.GetFromJsonAsync<Diagram[]>(ApplicationApi.Diagrams.Get.Request) ?? [];
    }

    private async Task OnAddNewTrendDiagram()
    {
        const string namePrefix = "New Trends Diagram";
        var matchingDiagramCount = _diagrams!.Count(d => d.Name.StartsWith(namePrefix)) + 1;
        
        var diagram = new Diagram
        {
            Name = $"{namePrefix} {matchingDiagramCount}",
            Description = "Provide a short description",
            Id = Guid.NewGuid(),
            Position = new Vector2(0, 0),
            CreationDate = DateTime.UtcNow,
            ModificationDate = DateTime.UtcNow,
            Zoom = 0,
        };
        var response = await Http.PostAsJsonAsync(ApplicationApi.Diagrams.Add.Request, diagram);
        diagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        _diagrams = _diagrams!.Concat([diagram]).ToArray();
        StateHasChanged();
    }
    
    private async Task EditDiagram(Diagram diagram)
    {
        var response = await Http.PatchAsJsonAsync(ApplicationApi.Diagrams.Edit.Request(diagram.Id), diagram);
        var changedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        var diagramIndex = _diagrams!.Index().Single(d => d.Item.Id == changedDiagram.Id).Index;
        _diagrams![diagramIndex] = changedDiagram;
        StateHasChanged();
    }
    
    private async Task DeleteDiagram(Diagram diagram)
    {
        var response = await Http.DeleteAsync(ApplicationApi.Diagrams.Remove.Request(diagram.Id));
        var removedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
        _diagrams = _diagrams!
            .Where(d => d.Id != removedDiagram.Id)
            .ToArray();
        StateHasChanged();
    }
}