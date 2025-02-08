using System.Net.Http.Json;

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
            _diagrams = await Http.GetFromJsonAsync<Diagram[]>(ApplicationPath.Diagrams.GetDiagramsRequest) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private async Task OnAddNewTrendDiagram()
    {
        await ReloadDiagrams();
    }
}