using Blazor.Diagrams;
using Blazor.Diagrams.Components;
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
}