namespace EtAlii.Adp.Client;

public class DiagramManager
{
    public Diagram? CurrentDiagram { get; private set; }

    private readonly ILogger _logger;
    public DiagramManager(
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<UserManager>();
    }

    public void SetCurrentDiagram(Diagram diagram)
    {
        _logger.LogInformation("Set current diagram to {DiagramName} with {DiagramIdentifier}", diagram.Name, diagram.Id);
        CurrentDiagram = diagram;
    }
    
    public void ClearCurrentDiagram()
    {
        _logger.LogInformation("Clearing current diagram");
        CurrentDiagram = null!;
    }
}