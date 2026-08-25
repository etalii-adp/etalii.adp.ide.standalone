namespace EtAlii.Adp.C4;

/// <summary>A boundary drawn around the elements inside one scope (Requirement 4.7).</summary>
/// <param name="Id">A synthetic id, since a boundary is drawn from an element rather than declared.</param>
/// <param name="Name">The scope element's name.</param>
/// <param name="Kind">What the boundary encloses, for its label: "Software System" or "Container".</param>
public sealed record C4Boundary(string Id, string Name, string Kind, C4Box Box);

/// <summary>Where everything on one view goes.</summary>
/// <param name="AuthoredPositionsIgnored">
/// True when the user has arranged this view by hand but the document declares
/// <c>autoLayout</c>, so the declaration won. The canvas says so rather than letting the
/// arrangement vanish without explanation (Requirement 8.4).
/// </param>
public sealed record C4Layout(
    IReadOnlyDictionary<string, C4Box> Boxes,
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
    /// <param name="authored">
    /// Positions the user arranged by hand, from the sidecar. They win over the computed
    /// arrangement - unless the document declares <c>autoLayout</c>, which is the author asking
    /// for a computed one explicitly (Requirements 8.3, 8.4).
    /// </param>
    public static C4Layout Compute(
        C4Workspace workspace,
        C4View view,
        C4Metrics metrics,
        IReadOnlyDictionary<string, C4SidecarPosition>? authored = null)
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

        // A hand-made arrangement wins over the computed one - except where the document asked
        // for autoLayout, which is the author saying they want it computed (Requirement 8.4).
        var hasAuthored = authored is { Count: > 0 } && authored.Keys.Any(sizes.ContainsKey);
        var authoredIgnored = hasAuthored && view.AutoLayout is not null;
        if (hasAuthored && !authoredIgnored)
        {
            foreach (var (id, position) in authored!)
            {
                if (sizes.TryGetValue(id, out var size))
                {
                    boxes[id] = new C4Box(Math.Round(position.X, 2), Math.Round(position.Y, 2), size.Width, size.Height);
                }
            }
        }

        var boundaries = BoundariesFor(workspace, view, members, boxes, metrics);
        boundaries = PushOutsiders(workspace, view, members, boxes, boundaries, metrics);
        return new C4Layout(boxes, boundaries, authoredIgnored);
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
            .Where(edge => ids.Contains(edge.SourceId) && ids.Contains(edge.DestinationId))
            .ToLookup(edge => edge.SourceId, edge => edge.DestinationId, StringComparer.OrdinalIgnoreCase);

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
        double nodeSeparation)
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
    /// Moves anything that is *not* inside a boundary out of it, and re-sizes the boundary
    /// afterwards. A boundary means "these are the parts of that system", so an external system
    /// drawn inside one says the opposite of what the diagram means - and the layered layout,
    /// which ranks by relationship distance, has no reason on its own to keep them apart
    /// (found by the manual pass).
    /// </summary>
    private static IReadOnlyList<C4Boundary> PushOutsiders(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        Dictionary<string, C4Box> boxes,
        IReadOnlyList<C4Boundary> boundaries,
        C4Metrics metrics)
    {
        if (boundaries.Count == 0 || view.ScopeId is not { } scopeId)
        {
            return boundaries;
        }

        var inside = members
            .Where(element => string.Equals(element.ParentId, scopeId, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var boundary = boundaries[0].Box;
        var outsiders = members.Where(element => !inside.Contains(element.Id)).Select(element => element.Id).ToArray();
        var pushedVertically = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in outsiders)
        {
            var box = boxes[id];
            if (!box.Overlaps(boundary))
            {
                continue;
            }

            // Out by the shortest route, so an element that merely clips the edge does not fly
            // across the diagram.
            var left = box.Right - boundary.X + metrics.NodeSeparation;
            var right = boundary.Right - box.X + metrics.NodeSeparation;
            var up = box.Bottom - boundary.Y + metrics.NodeSeparation;
            var down = boundary.Bottom - box.Y + metrics.NodeSeparation;
            var shortest = Math.Min(Math.Min(left, right), Math.Min(up, down));

            boxes[id] = shortest == left ? box with { X = Math.Round(box.X - left, 2) }
                : shortest == right ? box with { X = Math.Round(box.X + right, 2) }
                : shortest == up ? box with { Y = Math.Round(box.Y - up, 2) }
                : box with { Y = Math.Round(box.Y + down, 2) };
            pushedVertically[id] = shortest == up || shortest == down;
        }

        Spread(boxes, outsiders, pushedVertically, metrics.NodeSeparation);

        // The boundary is sized to its members, which did not move - but recomputing keeps this
        // honest if that ever changes.
        return BoundariesFor(workspace, view, members, boxes, metrics);
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

    /// <summary>
    /// Separates outsiders that landed on each other on the way out of a boundary.
    /// </summary>
    /// <remarks>
    /// Each one leaves by its own shortest route, knowing nothing of the others, so two that
    /// share an edge arrive at the same place: on the C4 worked example's container view the
    /// customer and the mainframe banking system both left through the top and ended up drawn
    /// one on top of the other.
    ///
    /// They are spread along the axis they were <em>not</em> pushed along, which is what makes
    /// this safe: the coordinate that carried an element clear of the boundary is the one left
    /// untouched, so separating them cannot walk one back inside.
    /// </remarks>
    private static void Spread(
        Dictionary<string, C4Box> boxes,
        IReadOnlyList<string> outsiders,
        Dictionary<string, bool> pushedVertically,
        double nodeSeparation)
    {
        if (pushedVertically.Count == 0)
        {
            return;
        }

        // Bounded, so a set that cannot be separated cannot spin either.
        for (var pass = 0; pass < outsiders.Count; pass++)
        {
            var moved = false;
            foreach (var (id, vertically) in pushedVertically)
            {
                foreach (var other in outsiders)
                {
                    if (string.Equals(other, id, StringComparison.OrdinalIgnoreCase) || !boxes[id].Overlaps(boxes[other]))
                    {
                        continue;
                    }

                    var box = boxes[id];
                    var blocker = boxes[other];
                    if (vertically)
                    {
                        var toLeft = box.Right - blocker.X + nodeSeparation;
                        var toRight = blocker.Right - box.X + nodeSeparation;
                        boxes[id] = toLeft <= toRight
                            ? box with { X = Math.Round(box.X - toLeft, 2) }
                            : box with { X = Math.Round(box.X + toRight, 2) };
                    }
                    else
                    {
                        var up = box.Bottom - blocker.Y + nodeSeparation;
                        var down = blocker.Bottom - box.Y + nodeSeparation;
                        boxes[id] = up <= down
                            ? box with { Y = Math.Round(box.Y - up, 2) }
                            : box with { Y = Math.Round(box.Y + down, 2) };
                    }

                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }
}
