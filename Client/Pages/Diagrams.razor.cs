using System.Net.Http.Json;
using BlazorApp.Shared;

namespace EtAlii.Adp.Client.Pages;

public partial class Diagrams
{
    private WeatherForecast[] _forecasts = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _forecasts = await Http.GetFromJsonAsync<WeatherForecast[]>("/api/WeatherForecast") ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}