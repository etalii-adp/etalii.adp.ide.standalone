using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Remove.Function)]
    public async Task<HttpResponseData> RemoveDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Delete, Route = ApplicationApi.Diagrams.Remove.Route)] HttpRequestData request, Guid id)
    {
        try
        {
#if !DEBUG              
            var claims = ClientPrincipal.Parse(request);
            if (claims.Identity?.IsAuthenticated != true)
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }
#endif

            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            // Fetch.
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var diagram = await context.Diagrams.SingleAsync(d => d.Id == (DiagramIdentifier)id);
            
            // Save.
            context.Entry(diagram).State = EntityState.Deleted;
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