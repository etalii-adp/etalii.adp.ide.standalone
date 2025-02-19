using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class LinkView : LinkModel
{
    public new LinkIdentifier Id { get; }

    public LinkView(Anchor source, Anchor target) : base(source, target)
    {
        Id = LinkIdentifier.NewIdentifier();
    }

    public LinkView(LinkIdentifier id, PortModel sourcePort, PortModel targetPort) : base(sourcePort, targetPort)
    {
        Id = id;
    }
}