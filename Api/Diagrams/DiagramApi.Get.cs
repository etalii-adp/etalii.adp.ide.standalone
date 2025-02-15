using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class DiagramApi
{
    private readonly ILogger _logger;
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.Anonymous;
    
    private readonly IDbContextFactory<AdpDbContext> _dbContextFactory;

    public DiagramApi(ILoggerFactory loggerFactory, IDbContextFactory<AdpDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        _logger = loggerFactory.CreateLogger<DiagramApi>();
        _logger.LogInformation("Initialized {FunctionApi}", nameof(DiagramApi));
    }
    
    [Function(ApplicationApi.Diagram.Content.Function)]
    //public async Task<HttpResponseData> GetContent([HttpTrigger(_authorizationLevel, HttpMethodName.Get, ApplicationApi.Diagram.Content.Route)] HttpRequestData request, Guid id)
    public async Task<HttpResponseData> GetContent([HttpTrigger(_authorizationLevel, HttpMethodName.Get, Route = ApplicationApi.Diagram.Content.Route)] HttpRequestData request, Guid id)
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
            
            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            var diagram = await context.Diagrams
                .Include(d => d.Nodes)
                .SingleAsync(d => d.Id == (DiagramIdentifier)id);

            // Clear the nodes diagram so that no circular dependencies are serialized.
            foreach (var node in diagram.Nodes) node.Diagram = null!;

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