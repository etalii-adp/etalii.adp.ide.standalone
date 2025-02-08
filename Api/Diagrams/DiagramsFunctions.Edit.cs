using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    [Function(ApplicationApi.Diagrams.Edit.Function)]
    public async Task<HttpResponseData> EditDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Put, ApplicationApi.Diagrams.Edit.Route)] HttpRequestData request, Guid id)
    {
        try
        {
            _logger.LogInformation("Handling {functionName}", request.FunctionContext.FunctionDefinition.Name);

            var changedDiagram = (await request.ReadFromJsonAsync<Diagram>())!;
            changedDiagram.ModificationDate = DateTime.UtcNow;

            var diagramIndex = _diagrams.Index().Single(d => d.Item.Id == changedDiagram.Id).Index;
            _diagrams[diagramIndex] = changedDiagram;

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(changedDiagram);
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