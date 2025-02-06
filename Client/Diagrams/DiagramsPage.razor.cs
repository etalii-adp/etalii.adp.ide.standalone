using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private Diagram[] _diagrams = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _diagrams = await Http.GetFromJsonAsync<Diagram[]>("/api/diagrams") ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}