using System.Net;
using System.Numerics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class DiagramsFunctions
{
    private readonly ILogger _logger;

    private static Diagram[] _diagrams = [];
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.User;
    
    public DiagramsFunctions(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramsFunctions>();
        _logger.LogInformation("Initialized Diagrams Functions");
    }

    [Function(ApplicationPath.Diagrams.GetDiagramsFunction)]
    public async Task<HttpResponseData> GetDiagrams([HttpTrigger(_authorizationLevel, HttpMethodName.Get)] HttpRequestData req)
    {
        _logger.LogInformation($"Handling {nameof(GetDiagrams)} request");

        if (_diagrams.Length == 0)
        {
            _diagrams = CreateTestDiagrams();
        }
        
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(_diagrams);
        return response;
    }

    private Diagram[] CreateTestDiagrams()
    {
        var rnd = new Random();
        return Enumerable.Range(1, 25).Select(index => CreateTestDiagram(index, rnd)).ToArray();
    }

    private Diagram CreateTestDiagram(int index, Random rnd)
    {
        return new Diagram
        {
            Id = Guid.NewGuid(),
            CreationDate = DateTime.Now.AddDays(-index * 2),
            ModificationDate = DateTime.Now.AddDays(-index),
            Name = $"Diagram {index}",
            Description = $"Diagram {index} description",
            Position = new Vector2(rnd.Next(-200, 200), rnd.Next(-200, 200)),
            Zoom = 0f
        };
    }
    
    [Function(nameof(ApplicationPath.Diagrams.AddDiagramFunction))]
    public async Task<HttpResponseData> AddDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Post)] HttpRequestData req)
    {
        _logger.LogInformation($"Handling {nameof(AddDiagram)} request");

        var rnd = new Random();
        var diagram = CreateTestDiagram(_diagrams.Length, rnd);
        _diagrams = _diagrams
            .Concat([diagram])
            .ToArray();
        
        return await Task.FromResult(req.CreateResponse(HttpStatusCode.OK));
    }
    
    [Function(nameof(ApplicationPath.Diagrams.RemoveDiagramFunction))]
    public async Task<HttpResponseData> RemoveDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Delete)] HttpRequestData req)
    {
        _logger.LogInformation($"Handling {nameof(RemoveDiagram)} request");
        
        return await Task.FromResult(req.CreateResponse(HttpStatusCode.OK));
    }
    
    [Function(nameof(ApplicationPath.Diagrams.EditDiagramFunction))]
    public async Task<HttpResponseData> EditDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Put)] HttpRequestData req)
    {
        _logger.LogInformation($"Handling {nameof(EditDiagram)} request");
        
        return await Task.FromResult(req.CreateResponse(HttpStatusCode.OK));
    }
}