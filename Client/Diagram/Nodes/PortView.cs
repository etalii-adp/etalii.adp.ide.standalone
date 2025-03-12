using Blazor.Diagrams.Core.Models;

namespace EtAlii.Adp.Client;

public class PortView : PortModel
{
    public NodeIdentifier NodeIdentifier { get; private set; }
    public PortView(string portId, NodeView parent, PortAlignment alignment)
        : base(portId, parent, alignment, ((NodeModel)parent).Position)
    {
        Locked = parent.Context.Diagram.IsReadOnly;
        NodeIdentifier = parent.Id;
    }
}