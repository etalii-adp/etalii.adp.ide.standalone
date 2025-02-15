namespace EtAlii.Adp;

public class NodeMoveChange : Change
{
    public required NodeIdentifier NodeId { get; init; }
     
    public required NodePosition NewPosition { get; init; }
    public required NodePosition OldPosition { get; init; }
    
    public static Change Apply(Diagram diagram, NodeIdentifier nodeId, NodePosition newPosition)
    {
        var node = diagram.Nodes.Single(n => n.Id == nodeId);
        
        var oldPosition = node.Position;
        node.Position = newPosition;
        
        return new NodeMoveChange
        {
            NodeId = nodeId,
            NewPosition = newPosition,
            OldPosition = oldPosition
        };
    }
}