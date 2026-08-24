namespace EtAlii.Adp.C4;

/// <summary>A boundary drawn around the elements inside one scope (Requirement 4.7).</summary>
/// <param name="Id">A synthetic id, since a boundary is drawn from an element rather than declared.</param>
/// <param name="Name">The scope element's name.</param>
/// <param name="Kind">What the boundary encloses, for its label: "Software System" or "Container".</param>
public sealed record C4Boundary(string Id, string Name, string Kind, C4Box Box);

/// <summary>Where everything on one view goes.</summary>
public sealed record C4Layout(
    IReadOnlyDictionary<string, C4Box> Boxes,
    IReadOnlyList<C4Boundary> Boundaries);

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
    public static C4Layout Compute(C4Workspace workspace, C4View view, C4Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(metrics);

        var members = C4RuleSet.MembersOf(workspace, view);
        if (members.Count == 0)
        {
            return new C4Layout(new Dictionary<string, C4Box>(StringComparer.OrdinalIgnoreCase), []);
        }

        var sizes = members.ToDictionary(
            element => element.Id,
            element => metrics.Measure(element.Name, TypeLineOf(element), element.Description),
            StringComparer.OrdinalIgnoreCase);

        var ranks = RankBy(workspace, view, members);
        var direction = view.AutoLayout?.Direction ?? "tb";
        var rankSeparation = view.AutoLayout?.RankSeparation ?? (int)metrics.RankSeparation;
        var nodeSeparation = view.AutoLayout?.NodeSeparation ?? (int)metrics.NodeSeparation;

        var boxes = Place(members, sizes, ranks, direction, rankSeparation, nodeSeparation);
        var boundaries = BoundariesFor(workspace, view, members, boxes, metrics);
        return new C4Layout(boxes, boundaries);
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

    /// <summary>
    /// How far each element sits from a source. Longest-path ranking over the relationships the
    /// view shows, which puts a person before the system they use; a cycle simply stops
    /// deepening rather than looping for ever.
    /// </summary>
    private static Dictionary<string, int> RankBy(C4Workspace workspace, C4View view, IReadOnlyList<C4Element> members)
    {
        var ids = members.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = view.Kind == C4ViewKind.Dynamic
            ? view.Interactions.Select(interaction => (interaction.SourceId, interaction.DestinationId))
            : workspace.Relationships.Select(relationship => (relationship.SourceId, relationship.DestinationId));
        var outgoing = edges
            .Where(edge => ids.Contains(edge.Item1) && ids.Contains(edge.Item2))
            .ToLookup(edge => edge.Item1, edge => edge.Item2, StringComparer.OrdinalIgnoreCase);

        var ranks = members.ToDictionary(element => element.Id, _ => 0, StringComparer.OrdinalIgnoreCase);

        // Relax the ranks as many times as there are elements: enough for the longest possible
        // acyclic path, and bounded so a cycle cannot spin.
        for (var pass = 0; pass < members.Count; pass++)
        {
            var moved = false;
            foreach (var element in members)
            {
                foreach (var destination in outgoing[element.Id])
                {
                    if (ranks[destination] < ranks[element.Id] + 1)
                    {
                        ranks[destination] = ranks[element.Id] + 1;
                        moved = true;
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return ranks;
    }

    /// <summary>
    /// Elements laid out rank by rank. Each rank is a row (or column, when the flow is
    /// horizontal), centred on the widest rank so the diagram reads as a whole rather than
    /// hugging one edge.
    /// </summary>
    private static Dictionary<string, C4Box> Place(
        IReadOnlyList<C4Element> members,
        Dictionary<string, C4Box> sizes,
        Dictionary<string, int> ranks,
        string direction,
        int rankSeparation,
        int nodeSeparation)
    {
        var horizontal = direction is "lr" or "rl";
        var reversed = direction is "rl" or "bt";
        var byRank = members
            .GroupBy(element => ranks[element.Id])
            .OrderBy(group => group.Key)
            .ToArray();

        var boxes = new Dictionary<string, C4Box>(StringComparer.OrdinalIgnoreCase);
        var rankOffset = 0.0;
        var rankExtents = new List<double>();

        foreach (var rank in byRank)
        {
            var elements = rank.ToArray();
            // The rank's thickness is its deepest member across the flow.
            var thickness = elements.Max(element => horizontal ? sizes[element.Id].Width : sizes[element.Id].Height);
            var along = elements.Sum(element => horizontal ? sizes[element.Id].Height : sizes[element.Id].Width)
                + nodeSeparation * (elements.Length - 1);
            rankExtents.Add(along);

            var alongOffset = -along / 2;
            foreach (var element in elements)
            {
                var size = sizes[element.Id];
                var acrossSize = horizontal ? size.Width : size.Height;
                var alongSize = horizontal ? size.Height : size.Width;
                // Centre each element within its rank's thickness, so a short box in a tall
                // rank sits on the rank's line rather than at its edge.
                var across = rankOffset + (thickness - acrossSize) / 2;

                boxes[element.Id] = horizontal
                    ? new C4Box(Math.Round(across, 2), Math.Round(alongOffset, 2), size.Width, size.Height)
                    : new C4Box(Math.Round(alongOffset, 2), Math.Round(across, 2), size.Width, size.Height);

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

    /// <summary>
    /// The boundary a view draws, when it has one: a container view encloses its system's
    /// containers, a component view its container's components (Requirements 6.5, 7.3). Sized
    /// to its contents with a margin, so it never clips what it contains (Requirement 8.6).
    /// </summary>
    private static IReadOnlyList<C4Boundary> BoundariesFor(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        IReadOnlyDictionary<string, C4Box> boxes,
        C4Metrics metrics)
    {
        if (view.Kind is not (C4ViewKind.Container or C4ViewKind.Component) || view.ScopeId is not { } scopeId)
        {
            return [];
        }

        var scope = workspace.Find(scopeId);
        if (scope is null)
        {
            return [];
        }

        var inside = members
            .Where(element => string.Equals(element.ParentId, scope.Id, StringComparison.OrdinalIgnoreCase))
            .Select(element => boxes[element.Id])
            .ToArray();

        if (inside.Length == 0)
        {
            return [];
        }

        var padding = metrics.BoundaryPadding;
        var x = inside.Min(box => box.X) - padding;
        var y = inside.Min(box => box.Y) - padding;
        var right = inside.Max(box => box.Right) + padding;
        // Extra room at the foot for the boundary's own label.
        var bottom = inside.Max(box => box.Bottom) + padding + metrics.FontSize * metrics.LineHeight;

        var kind = view.Kind == C4ViewKind.Container ? "Software System" : "Container";
        return [new C4Boundary($"boundary:{scope.Id}", scope.Name, kind, new C4Box(Math.Round(x, 2), Math.Round(y, 2), Math.Round(right - x, 2), Math.Round(bottom - y, 2)))];
    }
}
