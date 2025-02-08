using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    [Function(ApplicationApi.Diagrams.Remove.Function)]
    public async Task<HttpResponseData> RemoveDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Delete, Route = ApplicationApi.Diagrams.Remove.Route)] HttpRequestData request, Guid id)
    {
        try
        {
            _logger.LogInformation("Handling {functionName}", request.FunctionContext.FunctionDefinition.Name);

            var diagram = _diagrams.Single(d => d.Id == (DiagramIdentifier)id);

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