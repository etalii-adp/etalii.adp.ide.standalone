using System.Net;
using System.Security.Claims;
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
            var claims = ClientPrincipal.Parse(request, _logger);
#if !DEBUG              
            if (claims.Identity?.IsAuthenticated != true)
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }
#endif

            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            var userName = claims.Identity!.Name!;
            var externalIdentifier = claims.FindFirst(c => c.Type == ClaimTypes.Sid)!.Value;

            await using var context = await _dbContextFactory.CreateDbContextAsync();
            
            var user = await context.Users
                .Include(u => u.Diagrams)
                .SingleAsync(u => u.Name == userName && u.ExternalIdentifier == externalIdentifier); 
            var diagrams = user.Diagrams;

            foreach (var diagram in diagrams)
            {
                diagram.Owner = null!;
            }
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