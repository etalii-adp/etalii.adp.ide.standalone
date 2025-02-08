using System.Net;
using System.Numerics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    [Function(ApplicationApi.Diagrams.Get.Function)]
    public async Task<HttpResponseData> GetDiagrams([HttpTrigger(_authorizationLevel, HttpMethodName.Get)] HttpRequestData request)
    {
        try
        {
            _logger.LogInformation("Handling {functionName}", request.FunctionContext.FunctionDefinition.Name);

            if (_diagrams.Length == 0)
            {
                _diagrams = CreateTestDiagrams();
            }
        
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(_diagrams);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {requestMethod}", request.Method);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            await response.WriteAsJsonAsync(_diagrams);
            return response;
        }
    }
    
    private Diagram[] CreateTestDiagrams()
    {
        var rnd = new Random();
        return Enumerable.Range(1, 4).Select(index => CreateTestDiagram(index, rnd)).ToArray();
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
}