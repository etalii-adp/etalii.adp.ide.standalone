using Blazor.Diagrams;
using Blazor.Diagrams.Components;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;

namespace EtAlii.Adp.Client;

public class DiagramView : BlazorDiagram
{
    public required Diagram Diagram { get; init; }
    
    private new static readonly BlazorDiagramOptions Options = new()
    {
        AllowMultiSelection = true,
        Zoom = { Enabled = true },
        Links =
        {
            Factory = CreateLink,
            EnableSnapping = true,
            DefaultRouter = new NormalRouter(),
            DefaultPathGenerator = new SmoothPathGenerator()
        },
    };

    public DiagramView()
        : base(Options)
    {
        RegisterComponent<NodeView, NodeWidget>();
        RegisterComponent<LinkView, LinkWidget>();
    }

    private static BaseLinkModel CreateLink(Blazor.Diagrams.Core.Diagram diagram, ILinkable linkable, Anchor targetAnchor)
    {
        var source = new SinglePortAnchor((PortView)linkable);
        return new LinkView(source, targetAnchor);
    }
}