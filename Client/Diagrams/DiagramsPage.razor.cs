using System.Net.Http.Json;
using System.Numerics;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private Diagram[] _diagrams = [];

    protected override async Task OnInitializedAsync()
    {
        await ReloadDiagrams();
    }

    private async Task ReloadDiagrams()
    {
        try
        {
            _diagrams = await Http.GetFromJsonAsync<Diagram[]>(ApplicationApi.Diagrams.Get.Request) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private async Task OnAddNewTrendDiagram()
    {
        try
        {
            var diagram = new Diagram
            {
                Name = "Client created diagram",
                Description = "Client created diagram description",
                Id = Guid.NewGuid(),
                Position = new Vector2(1, 1),
                CreationDate = DateTime.Now,
                ModificationDate = DateTime.Now,
                Zoom = 0,
            };
            var response = await Http.PostAsJsonAsync(ApplicationApi.Diagrams.Add.Request, diagram);
            diagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
            _diagrams = _diagrams.Concat([diagram]).ToArray();
            await ReloadDiagrams();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
    
    private async Task EditDiagram(Diagram diagram)
    {
        try
        {
            var response = await Http.PatchAsJsonAsync(ApplicationApi.Diagrams.Edit.Request(diagram.Id), diagram);
            var changedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
            var diagramIndex = _diagrams.Index().Single(d => d.Item.Id == changedDiagram.Id).Index;
            _diagrams[diagramIndex] = changedDiagram;
            await ReloadDiagrams();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
    
    private async Task DeleteDiagram(Diagram diagram)
    {
        try
        {
            var response = await Http.DeleteAsync(ApplicationApi.Diagrams.Remove.Request(diagram.Id));
            var removedDiagram = (await response.Content.ReadFromJsonAsync<Diagram>())!;
            _diagrams = _diagrams
                .Where(d => d.Id != removedDiagram.Id)
                .ToArray();
            await ReloadDiagrams();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}