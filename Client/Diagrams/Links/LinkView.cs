using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class LinkView : LinkModel
{
    public new LinkIdentifier Id { get; private set; } = null!;
    public NodeIdentifier SourceNodeId { get; private set; } = null!;
    public NodeIdentifier TargetNodeId { get; private set; } = null!;

    // public LinkView(Anchor source, Anchor target) : base(source, target)
    // {
    // }
    //
    // public LinkView(string id, Anchor source, Anchor target) : base(id, source, target)
    // {
    // }

    public LinkView(LinkIdentifier id, NodeIdentifier sourceNodeId, NodeIdentifier targetNodeId, PortModel sourcePort, PortModel targetPort) : base(sourcePort, targetPort)
    {
        Initialize(id, sourceNodeId, targetNodeId);
    }

    public void Initialize(LinkIdentifier id, NodeIdentifier sourceNodeId, NodeIdentifier targetNodeId)
    {
        Id = id;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
    }

    // public LinkView(NodeModel sourceNode, NodeModel targetNode) : base(sourceNode, targetNode)
    // {
    // }
    //
    // public LinkView(string id, PortModel sourcePort, PortModel targetPort) : base(id, sourcePort, targetPort)
    // {
    // }
    //
    // public LinkView(string id, NodeModel sourceNode, NodeModel targetNode) : base(id, sourceNode, targetNode)
    // {
    // }
}