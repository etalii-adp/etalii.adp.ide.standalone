using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsApi
{
    [Function(ApplicationApi.Diagrams.Get.Function)]
    public async Task<HttpResponseData> GetDiagrams([HttpTrigger(_authorizationLevel, HttpMethodName.Get)] HttpRequestData request)
    {
        try
        {
            var user = ClientPrincipal.Parse(request);
            if (user.Identity?.IsAuthenticated != true)
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }
            
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var diagrams = await context.Diagrams.ToArrayAsync();
        
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diagrams);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {RequestMethod}", request.Method);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            return response;
        }
    }
}