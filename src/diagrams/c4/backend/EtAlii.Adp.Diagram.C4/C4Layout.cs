using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>A boundary drawn around the elements inside one scope (Requirement 4.7).</summary>
/// <param name="Id">A synthetic id, since a boundary is drawn from an element rather than declared: <see cref="IdPrefix"/> and the element's id.</param>
/// <param name="Name">The scope element's name.</param>
/// <param name="Kind">What the boundary encloses, for its label: "Software System" or "Container".</param>
public sealed record C4Boundary(string Id, string Name, string Kind, Rect Box)
{
    /// <summary>What a boundary's id begins with, the id of the element it is drawn for following it; the canvas reads it to name that element for a drop inside the boundary.</summary>
    public const string IdPrefix = "boundary:";
}

/// <summary>Where everything on one view goes.</summary>
/// <param name="AuthoredPositionsIgnored">
/// True when the user has arranged this view by hand but the document declares
/// <c>autoLayout</c>, so the declaration won. The canvas says so rather than letting the
/// arrangement vanish without explanation (Requirement 8.4).
/// </param>
public sealed record C4Layout(
    IReadOnlyDictionary<string, Rect> Boxes,
    IReadOnlyList<C4Boundary> Boundaries,
    bool AuthoredPositionsIgnored = false);

/// <summary>
/// Computes where a view's elements go. In the backend, always: the client draws what it is
/// told and never works out a position for itself, which is what lets the viewport query be
/// answered at all (c4-diagrams Requirement 8.1).
/// </summary>
/// <remarks>
/// A layered arrangement along the view's flow: elements are ranked by how far they sit from
/// something nothing points at, so a person ends up before the system they use and a database
/// after the application that reads it. Where the document declares <c>autoLayout</c>, its
/// direction and separations are honoured instead of ADP's defaults (Requirement 8.2).
/// </remarks>
public static class C4LayoutEngine
{
    /// <param name="view">The view to compute the layout for.</param>
    /// <param name="metrics">The metrics of the view.</param>
    /// <param name="authored">
    /// Positions the user arranged by hand, from the sidecar. They win over the computed
    /// arrangement - unless the document declares <c>autoLayout</c>, which is the author asking
    /// for a computed one explicitly (Requirements 8.3, 8.4).
    /// </param>
    /// <param name="workspace">The workspace to compute the layout for.</param>
    /// <param name="members">
    /// The elements the view shows, when the caller has them already; else they are read from the
    /// definition of the view's kind (<see cref="C4Definitions.Members"/>).
    /// </param>
    public static C4Layout Compute(
        C4Workspace workspace,
        C4View view,
        C4Metrics metrics,
        IReadOnlyDictionary<string, C4SidecarPosition>? authored = null,
        IReadOnlyList<C4Element>? members = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(metrics);

        members ??= C4Definitions.Members(workspace, C4Disl.FileOf(""), view);
        if (members.Count == 0)
        {
            return new C4Layout(new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase), []);
        }

        var direction = view.AutoLayout?.Direction ?? "tb";
        var rankSeparation = view.AutoLayout?.RankSeparation ?? (int)metrics.RankSeparation;
        var nodeSeparation = view.AutoLayout?.NodeSeparation ?? (int)metrics.NodeSeparation;

        if (view.Kind == C4ViewKind.Deployment)
        {
            return C4BoundaryPacker.ComputeNested(workspace, view, members, metrics, authored, direction, rankSeparation, nodeSeparation);
        }

        var sizes = members.ToDictionary(
            element => element.Id,
            element => MeasureOf(workspace, element, metrics),
            StringComparer.OrdinalIgnoreCase);

        var ranks = RankBy(workspace, view, members);

        var boxes = Place(members, sizes, ranks, direction, rankSeparation, nodeSeparation);

        // A hand-made arrangement wins over the computed one - except where the document asked
        // for autoLayout, which is the author saying they want it computed (Requirement 8.4).
        var hasAuthored = authored is { Count: > 0 } && authored.Keys.Any(sizes.ContainsKey);
        var authoredIgnored = hasAuthored && view.AutoLayout is not null;
        if (hasAuthored && !authoredIgnored)
        {
            foreach ((string id, C4SidecarPosition position) in authored!)
            {
                if (sizes.TryGetValue(id, out var size))
                {
                    boxes[id] = new Rect(Math.Round(position.X, 2), Math.Round(position.Y, 2), size.Width, size.Height);
                }
            }
        }

        var boundaries = C4BoundaryPacker.BoundariesFor(workspace, view, members, boxes, metrics);
        boundaries = C4BoundaryPacker.PushOutsiders(workspace, view, members, boxes, boundaries, metrics);
        return new C4Layout(boxes, boundaries, authoredIgnored);
    }

    /// <summary>
    /// The element as it is drawn. An instance declares no name, description or technology of
    /// its own - it is the container (or software system) it instantiates, placed on a node - so
    /// it shows the referenced element's. Without this every instance was a blank card
    /// reading only "[Container]". Everything else is drawn as declared.
    /// </summary>
    public static C4Element Displayed(C4Workspace workspace, C4Element element)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(element);

        if (element.Kind is not (C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
            || element.ReferencedId is not { } referencedId
            || workspace.Find(referencedId) is not { } referenced)
        {
            return element;
        }

        return element with
        {
            Name = element.Name.Length > 0 ? element.Name : referenced.Name,
            Description = element.Description.Length > 0 ? element.Description : referenced.Description,
            Technology = element.Technology.Length > 0 ? element.Technology : referenced.Technology,
        };
    }

    /// <summary>
    /// The relationships a deployment view draws between <paramref name="members"/>: the ones
    /// declared between deployment elements themselves, and the ones implied by instances - a
    /// relationship from container A to container B is drawn from every instance of A to every
    /// instance of B, which is how Structurizr draws them too.
    /// </summary>
    public static IEnumerable<C4Relationship> DeploymentRelationshipsOf(C4Workspace workspace, IReadOnlyList<C4Element> members)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(members);

        var ids = members.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var instancesOf = members
            .Where(element => element.ReferencedId is not null)
            .ToLookup(element => element.ReferencedId!, element => element.Id, StringComparer.OrdinalIgnoreCase);

        var drawn = new HashSet<(string, string)>();
        foreach (var relationship in workspace.Relationships)
        {
            if (ids.Contains(relationship.SourceId) && ids.Contains(relationship.DestinationId))
            {
                if (drawn.Add((relationship.SourceId, relationship.DestinationId)))
                {
                    yield return relationship;
                }

                continue;
            }

            foreach (var source in instancesOf[relationship.SourceId])
            {
                foreach (var destination in instancesOf[relationship.DestinationId])
                {
                    if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) && drawn.Add((source, destination)))
                    {
                        yield return relationship with { SourceId = source, DestinationId = destination };
                    }
                }
            }
        }
    }

    /// <summary>The bracketed line under an element's name: its type, and its technology where it has one (Requirement 4.2).</summary>
    public static string TypeLineOf(C4Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var type = element.Kind switch
        {
            C4ElementKind.Person => "Person",
            C4ElementKind.SoftwareSystem => "Software System",
            C4ElementKind.Container => "Container",
            C4ElementKind.Component => "Component",
            C4ElementKind.DeploymentNode => "Deployment Node",
            C4ElementKind.InfrastructureNode => "Infrastructure Node",
            C4ElementKind.ContainerInstance => "Container",
            C4ElementKind.SoftwareSystemInstance => "Software System",
            _ => element.Kind.ToString(),
        };

        return element.Technology.Length > 0 ? $"[{type}: {element.Technology}]" : $"[{type}]";
    }

    internal static Rect MeasureOf(C4Workspace workspace, C4Element element, C4Metrics metrics)
    {
        var displayed = Displayed(workspace, element);
        return metrics.Measure(displayed.Name, TypeLineOf(displayed), displayed.Description);
    }

    /// <summary>
    /// How far each element sits from a source. Longest-path ranking over the relationships the
    /// view shows, which puts a person before the system they use; a cycle simply stops
    /// deepening rather than looping for ever.
    /// </summary>
    private static Dictionary<string, int> RankBy(C4Workspace workspace, C4View view, IReadOnlyList<C4Element> members)
    {
        var edges = view.Kind == C4ViewKind.Dynamic
            ? view.Interactions.Select(interaction => (interaction.SourceId, interaction.DestinationId))
            : workspace.Relationships.Select(relationship => (relationship.SourceId, relationship.DestinationId));
        return Rank(members, edges);
    }

    /// <summary>
    /// Longest-path ranks of <paramref name="members"/> over <paramref name="edges"/>, by the shared
    /// layering's bounded relaxation (code-structure T239): enough rounds for the longest possible
    /// acyclic path, and bounded so a cycle cannot spin. Edges with an end outside the members are
    /// ignored.
    /// </summary>
    internal static Dictionary<string, int> Rank(IReadOnlyList<C4Element> members, IEnumerable<(string SourceId, string DestinationId)> edges)
    {
        // A C4 identifier matches without regard to case and the layering compares ordinally, so
        // each end is named as its member is.
        var ids = members.ToDictionary(element => element.Id, element => element.Id, StringComparer.OrdinalIgnoreCase);
        var named = edges
            .Where(edge => ids.ContainsKey(edge.SourceId) && ids.ContainsKey(edge.DestinationId))
            .Select(edge => (ids[edge.SourceId], ids[edge.DestinationId]));

        var layers = Layering.LongestPath(members.Select(element => element.Id), named, CyclePolicy.Relaxation);
        return new Dictionary<string, int>(layers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Elements laid out rank by rank. Each rank is a row (or column, when the flow is
    /// horizontal), centred on the widest rank so the diagram reads as a whole rather than
    /// hugging one edge.
    /// </summary>
    internal static Dictionary<string, Rect> Place(
        IReadOnlyList<C4Element> members,
        Dictionary<string, Rect> sizes,
        Dictionary<string, int> ranks,
        string direction,
        int rankSeparation,
        double nodeSeparation)
    {
        var horizontal = direction is "lr" or "rl";
        var reversed = direction is "rl" or "bt";
        var byRank = members
            .GroupBy(element => ranks[element.Id])
            .OrderBy(group => group.Key)
            .ToArray();

        var boxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var rankOffset = 0.0;
        //var rankExtents = new List<double>();

        foreach (var rank in byRank)
        {
            var elements = rank.ToArray();
            // The rank's thickness is its deepest member across the flow.
            var thickness = elements.Max(element => horizontal ? sizes[element.Id].Width : sizes[element.Id].Height);
            var along = elements.Sum(element => horizontal ? sizes[element.Id].Height : sizes[element.Id].Width)
                + nodeSeparation * (elements.Length - 1);
            //rankExtents.Add(along);

            var alongOffset = -along / 2;
            foreach (var element in elements)
            {
                var size = sizes[element.Id];
                var acrossSize = horizontal ? size.Width : size.Height;
                var alongSize = horizontal ? size.Height : size.Width;
                // Center each element within its rank's thickness, so a short box in a tall
                // rank sits on the rank's line rather than at its edge.
                var across = rankOffset + (thickness - acrossSize) / 2;

                boxes[element.Id] = horizontal
                    ? new Rect(Math.Round(across, 2), Math.Round(alongOffset, 2), size.Width, size.Height)
                    : new Rect(Math.Round(alongOffset, 2), Math.Round(across, 2), size.Width, size.Height);

                alongOffset += alongSize + nodeSeparation;
            }

            rankOffset += thickness + rankSeparation;
        }

        if (!reversed)
        {
            return boxes;
        }

        // rl and bt are the same layout read from the other end.
        var extent = rankOffset - rankSeparation;
        return boxes.ToDictionary(
            pair => pair.Key,
            pair => horizontal
                ? pair.Value with { X = Math.Round(extent - pair.Value.Right, 2) }
                : pair.Value with { Y = Math.Round(extent - pair.Value.Bottom, 2) },
            StringComparer.OrdinalIgnoreCase);
    }
}
