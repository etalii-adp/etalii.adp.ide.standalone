using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Edit.Function)]
    public async Task<HttpResponseData> EditDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Put, ApplicationApi.Diagrams.Edit.Route)] HttpRequestData request, Guid id)
    {
        try
        {
#if !DEBUG              
            var claims = ClientPrincipal.Parse(request, _logger);
            if (claims.Identity?.IsAuthenticated != true)
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }
#endif

            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            // Deserialize.
            var diagram = (await request.ReadFromJsonAsync<Diagram>())!;
            
            // Tag for modification.
            diagram.ModificationDate = DateTime.UtcNow;
            
            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            context.Entry(diagram).State = EntityState.Modified;
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