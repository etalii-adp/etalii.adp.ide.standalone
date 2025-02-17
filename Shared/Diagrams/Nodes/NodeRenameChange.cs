namespace EtAlii.Adp;

public class NodeRenameChange : Change
{
    public required NodeIdentifier NodeId { get; init; }
     
    public required string NewName { get; init; }
    public required string OldName { get; init; }
    
    public static Change Apply(Diagram diagram, NodeIdentifier nodeId, string oldName, string newName)
    {
        var node = diagram.Nodes.Single(n => n.Id == nodeId);
        
        node.Name = newName;
        
        return new NodeRenameChange
        {
            NodeId = nodeId,
            NewName = newName,
            OldName = oldName
        };
    }
}