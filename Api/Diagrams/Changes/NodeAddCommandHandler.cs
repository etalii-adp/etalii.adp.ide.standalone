using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeAddCommandHandler : CommandHandler<NodeAddCommand>
{
    protected override async Task Do(NodeAddCommand command, AdpDbContext context)
    {
        await Do(context, command.DiagramId, command.NewNodeId, command.NewPosition, command.NewName);
    }

    public async Task Do(
        AdpDbContext context, 
        DiagramIdentifier diagramId,
        NodeIdentifier nodeId,
        NodePosition nodePosition,
        string nodeName)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == diagramId);

        // Apply changes.
        var node = new Node
        {
            Diagram = diagram,
            Id = nodeId,
            Position = nodePosition,
            Name = nodeName
        };
        diagram.Nodes.Add(node);
        
        // Tag for modification and addition.
        context.Entry(diagram).State = EntityState.Modified;
        context.Entry(node).State = EntityState.Added;
    }

    protected override async Task Undo(NodeAddCommand command, AdpDbContext context)
    {
        await Undo(context, command.DiagramId, command.NewNodeId);
    }

    public async Task Undo(AdpDbContext context, DiagramIdentifier diagramId, NodeIdentifier nodeId)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == diagramId);

        var node = await context.Nodes.SingleAsync(n => n.Id == nodeId);

        //var sourceLinks = await context.Links.Where(l => l.SourceNode == node).ToArrayAsync();
        //var targetLinks = await context.Links.Where(l => l.TargetNode == node).ToArrayAsync();
        
        // Tag for deletion.
        context.Entry(diagram).State = EntityState.Modified;
        context.Entry(node).State = EntityState.Deleted;

        // foreach (var sourceLink in sourceLinks)
        // {
        //     context.Entry(sourceLink).State = EntityState.Deleted;
        // }
        //
        // foreach (var targetLink in targetLinks)
        // {
        //     context.Entry(targetLink).State = EntityState.Deleted;
        // }
    }
}