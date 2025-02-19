namespace EtAlii.Adp;

public class NodeRemoveChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required NodeIdentifier OldNodeId { get; init; }
    public required NodePosition OldNodePosition { get; init; }

    public static Change Apply(Diagram diagram, NodeIdentifier oldNodeId, NodePosition oldNodePosition)
    {
        var node = diagram.Nodes.Single(n => n.Id == oldNodeId);
        diagram.Nodes.Remove(node);
        
        return new NodeRemoveChange
        {
            DiagramId = diagram.Id, 
            OldNodeId = oldNodeId,
            OldNodePosition = oldNodePosition
        };
    }
}