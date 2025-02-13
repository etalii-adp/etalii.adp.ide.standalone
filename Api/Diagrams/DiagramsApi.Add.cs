using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Add.Function)]
    public async Task<HttpResponseData> AddDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Post, Route = ApplicationApi.Diagrams.Add.Function)] HttpRequestData request)
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

            // Deserialize.
            var diagram = (await request.ReadFromJsonAsync<Diagram>())!;
            
            // Tag for modification.
            diagram.ModificationDate = DateTime.UtcNow;

            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            context.Entry(diagram).State = EntityState.Added;
            await context.SaveChangesAsync();
        
            // Respond.
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagram);
            _logger.LogInformation("Handled {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
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