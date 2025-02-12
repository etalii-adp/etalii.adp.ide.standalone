using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Edit.Function)]
    public async Task<HttpResponseData> EditDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Put, ApplicationApi.Diagrams.Edit.Route)] HttpRequestData request, Guid id)
    {
        try
        {
            var user = ClientPrincipal.Parse(request);
            if (user.Identity?.IsAuthenticated != true)
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }

            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            // Deserialize.
            var diagram = (await request.ReadFromJsonAsync<Diagram>())!;
            
            // Tag for modification.
            diagram.ModificationDate = DateTime.UtcNow;
            
            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            context.Diagrams.Update(diagram);
            await context.SaveChangesAsync();

            // Respond.
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagram);
            _logger.LogTrace("Handled {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            return response;
        }
    }
}