namespace EtAlii.Adp.Client;

public class NodeFactory
{
    public Node Create(Diagram diagram, NodeIdentifier nodeId, NodePosition nodePosition, string nodeName)
    {
        var node = new Node
        {
            Id = nodeId, 
            Diagram = diagram,
            Position = nodePosition,
            Name = nodeName,
        };

        return node;
    }
}