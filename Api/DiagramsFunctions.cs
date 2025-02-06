using System.Net;
using System.Numerics;
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

        var result = Enumerable.Range(1, 25).Select(index => new Diagram
        {
            Id = Guid.NewGuid(),
            CreationDate = DateTime.Now.AddDays(-index * 2),
            ModificationDate = DateTime.Now.AddDays(-index),
            Name = $"Diagram {index}",
            Description = $"Diagram {index} description",
            Position = new Vector2(randomNumber.Next(-200, 200), randomNumber.Next(-200, 200)),
            Zoom = 0f
        }).ToArray();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}