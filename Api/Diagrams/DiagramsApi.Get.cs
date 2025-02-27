using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Get.Function)]
    public async Task<HttpResponseData> GetDiagrams([HttpTrigger(_authorizationLevel, HttpMethodName.Get, Route = ApplicationApi.Diagrams.Get.Route)] HttpRequestData request)
    {
        try
        {
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            if (!request.TryAuthentication(_logger, out var response, out var principal)) return response;
            
            var userName = principal.UserDetails;
            var externalIdentifier = principal.ExternalIdentifier;

            await using var context = await _dbContextFactory.CreateDbContextAsync();
            
            var user = await context.Users
                .Include(u => u.Diagrams)
                .SingleAsync(u => u.Name == userName && u.ExternalIdentifier == externalIdentifier); 
            var diagrams = user.Diagrams;

            // Clear the diagrams owner so that no circular dependencies are serialized.
            foreach (var diagram in diagrams) diagram.Owner = null!;
            
            response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagrams);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {RequestMethod}", request.Method);
            return request.HandleFailure(_logger, e);
        }
    }
}