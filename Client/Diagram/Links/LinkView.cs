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

    public LinkView(Link link, DiagramContext context, PortModel sourcePort, PortModel targetPort) : base(sourcePort, targetPort)
    {
        Locked = context.Diagram.IsReadOnly;
        Id = link.Id;
    }
}