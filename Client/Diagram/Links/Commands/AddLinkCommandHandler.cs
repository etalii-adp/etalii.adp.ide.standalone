namespace EtAlii.Adp.Client;

public class AddLinkCommandHandler : CommandHandler<LinkAddCommand>
{
    private readonly ILogger _logger;

    public AddLinkCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AddLinkCommandHandler>();
    }

    public static Command CreateCommand(Diagram diagram, 
        LinkIdentifier newLinkId, 
        NodeIdentifier sourceNodeId, 
        string sourcePort,
        NodeIdentifier targetNodeId,
        string targetPort)
    {
        return new LinkAddCommand
        {
            DiagramId = diagram.Id, 
            NewLinkId = newLinkId,
            SourceNodeId = sourceNodeId,
            SourcePort = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPort = targetPort,
        };
    }

    protected override Task Do(LinkAddCommand command, DiagramContext context)
    {
        _logger.LogInformation("Adding link: {SourcePort}@{SourceNode} to {TargetPort}{TargetNode}", command.SourcePort, command.SourceNodeId, command.TargetPort, command.TargetNodeId);

        return Task.CompletedTask;
    }

    protected override Task Undo(LinkAddCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing link: {SourcePort}@{SourceNode} to {TargetPort}{TargetNode}", command.SourcePort, command.SourceNodeId, command.TargetPort, command.TargetNodeId);
        
        return Task.CompletedTask;
    }
}