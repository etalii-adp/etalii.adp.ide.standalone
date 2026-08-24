using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Diagram;
using Google.Protobuf;

namespace EtAlii.Adp.C4;

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
    public const string RelationshipType = "c4/model+relationship";
    public const string BoundaryType = "c4/model+boundary";
    public const string ViewType = "c4/model+view";

    /// <summary>The id of the synthetic element carrying the view's title, legend and problems.</summary>
    public const string ViewElementId = "c4:view";

    private readonly C4Metrics _metrics;

    public C4ElementMapper(C4Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Everything a connection sees of <paramref name="view"/> inside <paramref name="viewport"/>.
    /// A node whose box the viewport merely touches counts as in view, and each in-view node
    /// brings the far end of its relationships and its enclosing boundary with it - or the line
    /// running off the edge of the screen would have nothing to anchor to.
    /// </summary>
    public IReadOnlyList<DiagramElement> Visible(C4Workspace workspace, C4View view, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var layout = C4LayoutEngine.Compute(workspace, view, _metrics);
        var members = C4RuleSet.MembersOf(workspace, view);
        var shown = members.Where(element => layout.Boxes.ContainsKey(element.Id)).ToArray();

        var inView = shown.Where(element => Intersects(layout.Boxes[element.Id], viewport)).ToHashSet();
        var relationships = RelationshipsOf(workspace, view, layout).ToArray();

        // One hop: whatever an on-screen element's line reaches.
        var delivered = inView.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (relationship, _, _) in relationships)
        {
            if (delivered.Contains(relationship.SourceId) || delivered.Contains(relationship.DestinationId))
            {
                delivered.Add(relationship.SourceId);
                delivered.Add(relationship.DestinationId);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var element in shown.Where(element => delivered.Contains(element.Id)))
        {
            elements.Add(ToElement(element, layout.Boxes[element.Id], workspace.Styles));
        }

        foreach (var (relationship, source, destination) in relationships)
        {
            if (delivered.Contains(relationship.SourceId) && delivered.Contains(relationship.DestinationId))
            {
                elements.Add(ToElement(relationship, source, destination));
            }
        }

        // A boundary comes with whatever it encloses, so a zoomed-in view still shows the box
        // it is inside rather than floating elements.
        foreach (var boundary in layout.Boundaries)
        {
            elements.Add(ToElement(boundary));
        }

        elements.Add(ViewElement(workspace, view));
        return elements;
    }

    /// <summary>The view's own element: its title, the key describing the notation in use, and its problems.</summary>
    public DiagramElement ViewElement(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var payload = new C4ViewPayload
        {
            Title = TitleOf(workspace, view),
            ViewKind = view.Kind.ToString(),
            ViewKey = view.Key,
        };

        payload.Legend.AddRange(LegendFor(workspace, view));
        payload.Problems.AddRange(C4RuleSet.Validate(workspace).Select(problem => new C4Problem
        {
            ElementId = problem.Location is DiagramProblemLocation.ElementId elementId ? elementId.Id : "",
            RuleId = problem.RuleId,
            Message = problem.Message,
            Line = problem.Location is DiagramProblemLocation.Line line ? line.Number : 0,
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
    /// &lt;scope&gt;". A title the document declares wins (Requirements 5.4, 9.7).
    /// </summary>
    public static string TitleOf(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        if (view.Title is { Length: > 0 } declared)
        {
            return declared;
        }

        var kind = view.Kind switch
        {
            C4ViewKind.SystemLandscape => "System Landscape",
            C4ViewKind.SystemContext => "System Context",
            C4ViewKind.Container => "Container",
            C4ViewKind.Component => "Component",
            C4ViewKind.Dynamic => "Dynamic",
            C4ViewKind.Deployment => "Deployment",
            _ => view.Kind.ToString(),
        };

        var scope = view.ScopeId is { } id ? workspace.Find(id)?.Name : null;
        if (view.Kind == C4ViewKind.SystemLandscape)
        {
            return workspace.Name.Length > 0 ? $"System Landscape diagram for {workspace.Name}" : "System Landscape diagram";
        }

        if (view.Kind == C4ViewKind.Deployment && view.Environment is { Length: > 0 } environment)
        {
            return scope is { Length: > 0 } ? $"Deployment diagram for {scope} - {environment}" : $"Deployment diagram - {environment}";
        }

        return scope is { Length: > 0 } ? $"{kind} diagram for {scope}" : $"{kind} diagram";
    }

    /// <summary>
    /// The key, built from the notation actually in use rather than a fixed list - so a
    /// document that overrides the palette gets a legend that tells the truth about it
    /// (Requirements 4.9, 9.8).
    /// </summary>
    public static IReadOnlyList<C4LegendEntry> LegendFor(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var entries = new List<C4LegendEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in C4RuleSet.MembersOf(workspace, view))
        {
            var style = C4Theme.Apply(C4Theme.For(element), element, workspace.Styles);
            var label = element.IsExternal
                ? $"{C4Theme.KindTag(element.Kind)} (external)"
                : C4Theme.KindTag(element.Kind);

            if (seen.Add(label))
            {
                entries.Add(new C4LegendEntry { Label = label, Style = style });
            }
        }

        return entries;
    }

    private DiagramElement ToElement(C4Element element, C4Box box, IReadOnlyList<C4ElementStyle> styles)
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

    private static DiagramElement ToElement(C4Relationship relationship, C4Box source, C4Box destination)
    {
        var payload = new C4RelationshipPayload
        {
            SourceId = relationship.SourceId,
            DestinationId = relationship.DestinationId,
            Description = relationship.Description,
            Technology = relationship.Technology,
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
            relationship.Id,
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

    /// <summary>The relationships this view draws, paired with the boxes at their two ends.</summary>
    private static IEnumerable<(C4Relationship Relationship, C4Box Source, C4Box Destination)> RelationshipsOf(
        C4Workspace workspace,
        C4View view,
        C4Layout layout)
    {
        if (view.Kind == C4ViewKind.Dynamic)
        {
            // A dynamic view draws the interactions it declares, in their order, rather than
            // every relationship the model happens to carry.
            foreach (var interaction in view.Interactions)
            {
                if (layout.Boxes.TryGetValue(interaction.SourceId, out var from) &&
                    layout.Boxes.TryGetValue(interaction.DestinationId, out var to))
                {
                    yield return (
                        new C4Relationship(interaction.SourceId, interaction.DestinationId, interaction.Description, "", [], interaction.Line)
                        {
                        },
                        from,
                        to);
                }
            }

            yield break;
        }

        foreach (var relationship in workspace.Relationships)
        {
            if (layout.Boxes.TryGetValue(relationship.SourceId, out var from) &&
                layout.Boxes.TryGetValue(relationship.DestinationId, out var to))
            {
                yield return (relationship, from, to);
            }
        }
    }

    private static bool Intersects(C4Box box, DiagramViewport viewport) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;
}
