namespace EtAlii.Adp;

public class NodeAddChange : Change
{
    public required DiagramIdentifier DiagramId { get; init; }
    public required NodeIdentifier NewNodeId { get; init; }
    public required NodePosition NewPosition { get; init; }
    
    public static Change Apply(Diagram diagram, NodePosition position, NodeIdentifier nodeId)
    {
        var node = new Node
        {
            Id = nodeId, 
            Diagram = diagram,
            Position = position
        };
        diagram.Nodes.Add(node);
        
        return new NodeAddChange
        {
            DiagramId = diagram.Id, 
            NewNodeId = node.Id,
            NewPosition = position
        };
    }
}