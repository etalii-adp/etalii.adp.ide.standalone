using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    [Function(ApplicationApi.Diagrams.Add.Function)]
    public async Task<HttpResponseData> AddDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Post, Route = ApplicationApi.Diagrams.Add.Function)] HttpRequestData request)
    {
        try
        {
            _logger.LogInformation("Handling {functionName}", request.FunctionContext.FunctionDefinition.Name);

            var diagram = (await request.ReadFromJsonAsync<Diagram>())!;
            diagram.ModificationDate = DateTime.UtcNow;
            
            _diagrams = _diagrams
                .Concat([diagram])
                .ToArray();
        
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagram);
            _logger.LogTrace("Handled {functionName}", request.FunctionContext.FunctionDefinition.Name);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {functionName}", request.FunctionContext.FunctionDefinition.Name);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            await response.WriteAsJsonAsync(_diagrams);
            return response;
        }
    }
}