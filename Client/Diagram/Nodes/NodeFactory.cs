namespace EtAlii.Adp.Client;

public class NodeFactory // ForDependencygraph, should become NodeFactory
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

        var typeGroup = new TagGroup
        {
            Id = TagGroupIdentifier.NewIdentifier(), Name = "Type",
            //Order = 0
        };
        node.TagGroups.Add(typeGroup);

        // var descriptionValue = new StringBlockValue
        // {
        //     Id = StringValue.NewIdentifier(), 
        //     Name = "Description",
        //     Order = 1,
        //     IsBlock = true
        // };
        //node.TagGroups.Add(typeGroup);

        return node;
    }
}