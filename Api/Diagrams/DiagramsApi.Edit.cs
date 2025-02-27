using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Edit.Function)]
    public async Task<HttpResponseData> EditDiagram([HttpTrigger(_authorizationLevel, HttpMethodName.Put, Route = ApplicationApi.Diagrams.Edit.Route)] HttpRequestData request, Guid id)
    {
        try
        {
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            if (!request.TryAuthentication(_logger, out var response, out _)) return response;

            // Deserialize.
            var diagram = (await request.ReadFromJsonAsync<Diagram>())!;

            if (diagram.Id != id)
            {
                _logger.LogError("Unable to handle {FunctionName} - mismatching Identifier {DiagramIdentifier}", request.FunctionContext.FunctionDefinition.Name, id);
                return request.CreateResponse(HttpStatusCode.Conflict);
            }
            
            // Tag for modification.
            diagram.ModificationDate = DateTime.UtcNow;
            
            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            context.Entry(diagram).State = EntityState.Modified;
            await context.SaveChangesAsync();

            // Respond.
            response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagram);
            _logger.LogInformation("Handled {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
            return request.HandleFailure(_logger, e);
        }
    }
}