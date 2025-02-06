using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private WeatherForecast[] _diagrams = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _diagrams = await HttpClientJsonExtensions.GetFromJsonAsync<WeatherForecast[]>(Http, "/api/diagrams") ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}