using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    private readonly ILogger _logger;
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.User;
    
    private readonly IDbContextFactory<AdpDbContext> _dbContextFactory;

    public DiagramsFunctions(ILoggerFactory loggerFactory, IDbContextFactory<AdpDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        _logger = loggerFactory.CreateLogger<DiagramsFunctions>();
        _logger.LogInformation("Initialized {FunctionApi}", nameof(DiagramsFunctions));
    }
}