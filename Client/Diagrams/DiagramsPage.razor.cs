using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public partial class DiagramsPage
{
    private WeatherForecast[] _forecasts = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _forecasts = await HttpClientJsonExtensions.GetFromJsonAsync<WeatherForecast[]>(Http, "/api/WeatherForecast") ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}