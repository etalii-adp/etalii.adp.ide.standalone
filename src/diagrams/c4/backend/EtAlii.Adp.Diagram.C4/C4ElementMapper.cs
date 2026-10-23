using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Turns a laid-out, validated view into the core element vocabulary. Elements, relationships,
/// boundaries and the view's own furniture all travel as <see cref="DiagramElement"/> with a
/// C4 payload in the contract's <c>Any</c>; nothing in the core contract is extended
/// (c4-diagrams Requirement 4, design "Deviations").
/// </summary>
public sealed class C4ElementMapper
{
    /// <summary>The mime-style element kinds C4 puts on the wire.</summary>
    public const string NodeType = "c4/model+node";
    [Relation]
    public const string RelationshipType = "c4/model+relationship";
    public const string BoundaryType = "c4/model+boundary";
    public const string ViewType = "c4/model+view";

    /// <summary>The id of the synthetic element carrying the view's title and legend.</summary>
    private const string ViewElementId = "c4:view";

    private readonly C4Metrics _metrics;
    private readonly C4LayoutSidecar _sidecar;

    public C4ElementMapper(C4Metrics metrics, C4LayoutSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(sidecar);
        _metrics = metrics;
        _sidecar = sidecar;
    }

    /// <summary>
    /// Everything a connection sees of <paramref name="view"/> inside <paramref name="viewport"/>.
    /// A node whose box the viewport merely touches counts as in view, and each in-view node
    /// brings the far end of its relationships and its enclosing boundary with it - or the line
    /// running off the edge of the screen would have nothing to anchor to.
    /// </summary>
    /// <remarks>
    /// What the view draws - its members, its lines, its title and its legend - is the definition's
    /// (<see cref="C4Definitions.Drawing"/>); where it goes, the boundaries and the viewport filter are this
    /// class's and the layout's (c4-disl-fbl Requirements 3.4 and 3.5).
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(C4Workspace workspace, C4View view, DiagramViewport viewport, string? bodyPath = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var drawing = C4Definitions.Drawing(workspace, C4Disl.FileOf(bodyPath ?? ""), view);

        // Whatever the user arranged by hand, layered over the computed arrangement.
        var authored = bodyPath is { Length: > 0 } ? _sidecar.Read(bodyPath, view.Key) : null;
        var layout = C4LayoutEngine.Compute(workspace, view, _metrics, authored, drawing.Members);
        var shown = drawing.Members.Where(element => layout.Boxes.ContainsKey(element.Id)).ToArray();

        var inView = shown.Where(element => viewport.Intersects(layout.Boxes[element.Id])).ToHashSet();

        // A line is drawn where both its ends have a box.
        var lines = drawing.Lines
            .Where(line => layout.Boxes.ContainsKey(line.SourceId) && layout.Boxes.ContainsKey(line.DestinationId))
            .ToArray();

        // One hop: whatever an on-screen element's line reaches.
        var delivered = inView.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (delivered.Contains(line.SourceId) || delivered.Contains(line.DestinationId))
            {
                delivered.Add(line.SourceId);
                delivered.Add(line.DestinationId);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var element in shown.Where(element => delivered.Contains(element.Id)))
        {
            elements.Add(ToElement(C4LayoutEngine.Displayed(workspace, element), layout.Boxes[element.Id], workspace.Styles));
        }

        foreach (var line in lines)
        {
            if (delivered.Contains(line.SourceId) && delivered.Contains(line.DestinationId))
            {
                elements.Add(ToElement(line, layout.Boxes[line.SourceId], layout.Boxes[line.DestinationId]));
            }
        }

        // A boundary comes with whatever it encloses, so a zoomed-in view still shows the box
        // it is inside rather than floating elements.
        foreach (var boundary in layout.Boundaries)
        {
            elements.Add(ToElement(boundary));
        }

        elements.Add(ViewElement(workspace, view, drawing));
        return elements;
    }

    /// <summary>The view's own element: its title and the key describing the notation in use.</summary>
    /// <remarks>
    /// Deliberately carries no problems of its own. C4's rules report through core's
    /// <c>IDiagramValidator</c>, and core already pushes the project's problems to every
    /// connection with an element-id location, so the canvas reads them from there. A second
    /// copy here would be a second thing to keep in step, and the two would drift apart.
    /// </remarks>
    private static DiagramElement ViewElement(C4Workspace workspace, C4View view, C4Drawing drawing)
    {
        var payload = new C4ViewPayload
        {
            Title = drawing.Title,
            ViewKind = view.Kind.ToString(),
            ViewKey = view.Key,
        };

        // The key's texts are the definition's; its swatches are the theme's, as the cards are, so a
        // document that overrides the palette gets a legend that tells the truth about it
        // (Requirements 4.9, 9.8).
        payload.Legend.AddRange(drawing.Legend.Select(entry => new C4LegendEntry
        {
            Label = entry.Label,
            Style = C4Theme.Apply(C4Theme.For(entry.First), entry.First, workspace.Styles),
        }));

        return new DiagramElement(
            ViewElementId,
            0,
            0,
            ViewType,
            $"type.googleapis.com/{C4ViewPayload.Descriptor.FullName}",
            payload.ToByteArray());
    }

    /// <summary>
    /// The title, which C4 requires on every diagram and words as "&lt;type&gt; diagram for
    /// &lt;scope&gt;": the definition's (DISL 6.13). A title the document declares wins (Requirements 5.4, 9.7).
    /// </summary>
    public static string TitleOf(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);
        return C4Definitions.Drawing(workspace, C4Disl.FileOf(""), view).Title;
    }

    private DiagramElement ToElement(C4Element element, Rect box, IReadOnlyList<C4ElementStyle> styles)
    {
        var payload = new C4ElementPayload
        {
            Kind = (C4ElementKindProto)((int)element.Kind + 1),
            Name = element.Name,
            Description = element.Description,
            Technology = element.Technology,
            TypeLine = C4LayoutEngine.TypeLineOf(element),
            External = element.IsExternal,
            ParentId = element.ParentId ?? "",
            Width = box.Width,
            Height = box.Height,
            Style = C4Theme.Apply(C4Theme.For(element), element, styles),
        };

        return new DiagramElement(
            element.Id,
            box.CenterX,
            box.CenterY,
            NodeType,
            $"type.googleapis.com/{C4ElementPayload.Descriptor.FullName}",
            payload.ToByteArray());
    }

    private static DiagramElement ToElement(C4DrawnLine line, Rect source, Rect destination)
    {
        var payload = new C4RelationshipPayload
        {
            SourceId = line.SourceId,
            DestinationId = line.DestinationId,
            Description = line.Description,
            Technology = line.Technology,
            InteractionOrder = line.Order,
            SourceX = source.CenterX,
            SourceY = source.CenterY,
            SourceWidth = source.Width,
            SourceHeight = source.Height,
            DestinationX = destination.CenterX,
            DestinationY = destination.CenterY,
            DestinationWidth = destination.Width,
            DestinationHeight = destination.Height,
        };

        return new DiagramElement(
            line.Id,
            (source.CenterX + destination.CenterX) / 2,
            (source.CenterY + destination.CenterY) / 2,
            RelationshipType,
            $"type.googleapis.com/{C4RelationshipPayload.Descriptor.FullName}",
            payload.ToByteArray());
    }

    private static DiagramElement ToElement(C4Boundary boundary)
    {
        var payload = new C4BoundaryPayload
        {
            Name = boundary.Name,
            Kind = boundary.Kind,
            Width = boundary.Box.Width,
            Height = boundary.Box.Height,
        };

        return new DiagramElement(
            boundary.Id,
            boundary.Box.CenterX,
            boundary.Box.CenterY,
            BoundaryType,
            $"type.googleapis.com/{C4BoundaryPayload.Descriptor.FullName}",
            payload.ToByteArray());
    }
}
