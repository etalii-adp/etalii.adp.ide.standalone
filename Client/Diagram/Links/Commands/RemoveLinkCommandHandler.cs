namespace EtAlii.Adp.Client;

public class RemoveLinkCommandHandler : CommandHandler<LinkRemoveCommand>
{
    private readonly ILogger _logger;

    public RemoveLinkCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RemoveLinkCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext context, 
        LinkIdentifier oldLinkId, 
        NodeIdentifier sourceNodeId, 
        string sourcePort,
        NodeIdentifier targetNodeId,
        string targetPort)
    {
        return new LinkRemoveCommand
        {
            DiagramId = context.Diagram.Id, 
            OldLinkId = oldLinkId,
            SourceNodeId = sourceNodeId,
            SourcePort = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPort = targetPort,
        };
    }

    protected override Task Do(LinkRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing link: {SourcePort}@{SourceNode} to {TargetPort}{TargetNode}", command.SourcePort, command.SourceNodeId, command.TargetPort, command.TargetNodeId);

        var link = context.View.Links.OfType<LinkView>().Single(l => l.Id == command.OldLinkId);
        context.View.Links.Remove(link);

        return Task.CompletedTask;
    }

    protected override Task Undo(LinkRemoveCommand command, DiagramContext context)
    {
        _logger.LogInformation("Removing link: {SourcePort}@{SourceNode} to {TargetPort}{TargetNode}", command.SourcePort, command.SourceNodeId, command.TargetPort, command.TargetNodeId);
        
        return Task.CompletedTask;
    }
}