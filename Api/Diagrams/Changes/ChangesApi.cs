using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class ChangesApi
{
    private readonly ILogger _logger;
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.Anonymous;
    
    private readonly IDbContextFactory<AdpDbContext> _dbContextFactory;
    private readonly ICommandHandler[] _commandHandlers;

    public ChangesApi(
        ILoggerFactory loggerFactory, 
        IDbContextFactory<AdpDbContext> dbContextFactory,
        IEnumerable<ICommandHandler> commandHandlers)
    {
        _dbContextFactory = dbContextFactory;
        _commandHandlers = commandHandlers.ToArray();

        _logger = loggerFactory.CreateLogger<DiagramsApi>();
        _logger.LogInformation("Initialized {FunctionApi}", nameof(ChangesApi));
    }
    
    [Function(ApplicationApi.Diagrams.Changes.Function)]
    public async Task<HttpResponseData> Update([HttpTrigger(_authorizationLevel, HttpMethodName.Post, Route = ApplicationApi.Diagrams.Changes.Route)] HttpRequestData request, Guid id)
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

            // // Deserialize.
            var commands = (await request.ReadFromJsonAsync<Command[]>())!;

            await using var context = await _dbContextFactory.CreateDbContextAsync();

            foreach (var command in commands)
            {
                var handler = _commandHandlers.Single(h => h.CommandType == command.GetType());
                await handler.Apply(command, context);
            }
            
            await context.SaveChangesAsync();
            
            // Respond.
            var response = request.CreateResponse(HttpStatusCode.OK);
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