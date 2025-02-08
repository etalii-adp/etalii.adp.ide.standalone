using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public partial class DiagramsFunctions
{
    private readonly ILogger _logger;

    private static Diagram[] _diagrams = [];
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.User;
    
    public DiagramsFunctions(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramsFunctions>();
        _logger.LogInformation("Initialized Diagrams Functions");
    }
}