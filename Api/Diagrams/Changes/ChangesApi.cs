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
        // We need to have access to the transaction in the catch clause.
        // Hence, we need to create the db context before the try.
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var executionStrategy = context.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

                if (!request.TryAuthentication(_logger, out var response, out _)) return response;

                // // Deserialize.
                var commands = (await request.ReadFromJsonAsync<Command[]>())!;

                foreach (var command in commands)
                {
                    var handler = _commandHandlers.Single(h => h.CommandType == command.GetType());
                    await handler.Apply(command, context);
                }

                // Let's save.
                await context.SaveChangesAsync();

                // All is fine, now let's commit the transaction.
                await transaction.CommitAsync();

                // Respond.
                response = request.CreateResponse(HttpStatusCode.OK);
                _logger.LogInformation("Handled {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
                return response;
            }
            catch (Exception e)
            {
                // We do not want any changes to the database as we inform the client of a failure.
                await transaction.RollbackAsync();

                _logger.LogError(e, "Unable to handle {FunctionName}", request.FunctionContext.FunctionDefinition.Name);
                return request.HandleFailure(_logger, e);
            }
        });
    }
}