using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class DiagramsFunctions
{
    private readonly ILogger _logger;

    public DiagramsFunctions(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramsFunctions>();
        _logger.LogInformation("Initialized Diagrams Functions");
    }

    [Function("Diagrams")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        _logger.LogInformation("Fetching diagrams");

        var randomNumber = new Random();
        int temp;

        var result = Enumerable.Range(1, 25).Select(index => new WeatherForecast
        {
            Date = DateTime.Now.AddDays(index),
            TemperatureC = temp = randomNumber.Next(-20, 55),
            Summary = GetSummary(temp)
        }).ToArray();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);

        return response;
    }

    private string GetSummary(int temp)
    {
        var summary = temp switch
        {
            >= 32 => "Hot",
            <= 16 and > 0 => "Cold",
            <= 0 => "Freezing",
            _ => "Mild"
        };

        return summary;
    }
}