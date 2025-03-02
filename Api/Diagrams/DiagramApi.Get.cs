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
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
            
            if (!request.TryAuthentication(_logger, out var response, out _)) return response;

            // Save.
            await using var context = await _dbContextFactory.CreateDbContextAsync();

            var diagram = await context.Diagrams
                .Include(d => d.Nodes)
                .ThenInclude(n => n.TagGroups)
                .ThenInclude(g => g.Tags)
                .Include(d => d.Links)
                .ThenInclude(l => l.TagGroups)
                .ThenInclude(g => g.Tags)
                .SingleAsync(d => d.Id == (DiagramIdentifier)id);

            // Clear the nodes and links diagram property so that no circular dependencies are serialized.
            foreach (var node in diagram.Nodes)
            {
                node.Diagram = null!;
                foreach (var group in node.TagGroups)
                {
                    group.Node = null!;
                    foreach (var tag in group.Tags)
                    {
                        tag.TagGroup = null!;
                    }
                }
            }
            foreach (var link in diagram.Links)
            {
                link.SourceNode.Diagram = null!;
                link.TargetNode.Diagram = null!;
                link.Diagram = null!;
                foreach (var group in link.TagGroups)
                {
                    group.Link = null!;
                    foreach (var tag in group.Tags)
                    {
                        tag.TagGroup = null!;
                    }
                }
            }

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