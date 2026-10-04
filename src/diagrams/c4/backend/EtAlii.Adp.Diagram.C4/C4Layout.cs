using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4;

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
    private const double Tolerance = 0.000001f;

    /// <param name="view">The view to compute the layout for.</param>
    /// <param name="metrics">The metrics of the view.</param>
    /// <param name="authored">
    /// Positions the user arranged by hand, from the sidecar. They win over the computed
    /// arrangement - unless the document declares <c>autoLayout</c>, which is the author asking
    /// for a computed one explicitly (Requirements 8.3, 8.4).
    /// </param>
    /// <param name="workspace">The workspace to compute the layout for.</param>
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

        var direction = view.AutoLayout?.Direction ?? "tb";
        var rankSeparation = view.AutoLayout?.RankSeparation ?? (int)metrics.RankSeparation;
        var nodeSeparation = view.AutoLayout?.NodeSeparation ?? (int)metrics.NodeSeparation;

        if (view.Kind == C4ViewKind.Deployment)
        {
            return ComputeNested(workspace, view, members, metrics, authored, direction, rankSeparation, nodeSeparation);
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
                    boxes[id] = new C4Box(Math.Round(position.X, 2), Math.Round(position.Y, 2), size.Width, size.Height);
                }
            }
        }

        var boundaries = BoundariesFor(workspace, view, members, boxes, metrics);
        boundaries = PushOutsiders(workspace, view, members, boxes, boundaries, metrics);
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

    private static C4Box MeasureOf(C4Workspace workspace, C4Element element, C4Metrics metrics)
    {
        var displayed = Displayed(workspace, element);
        return metrics.Measure(displayed.Name, TypeLineOf(displayed), displayed.Description);
    }

    /// <summary>
    /// A deployment view's layout: nested rather than flat. Deployment elements are joined by
    /// what runs inside what, not by relationships, so the flat ranking put every one of them in
    /// rank 0 - one long row with nothing inside anything (c4-diagrams Requirement 9.2). Here a
    /// deployment node that hosts something on the view is drawn as a boundary around what it
    /// hosts, to any depth, and the siblings inside each node are ranked along the
    /// relationships between them as a flat view is.
    /// </summary>
    /// <remarks>
    /// A position the user arranged is honoured only for an element no node encloses: one
    /// inside a node is placed by it, or a drag could carry it out of the node it runs on.
    /// </remarks>
    private static C4Layout ComputeNested(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        C4Metrics metrics,
        IReadOnlyDictionary<string, C4SidecarPosition>? authored,
        string direction,
        int rankSeparation,
        int nodeSeparation)
    {
        var byId = members.ToDictionary(element => element.Id, StringComparer.OrdinalIgnoreCase);

        // The nearest ancestor that is on the view: a node the view excludes does not stop what
        // it hosts from being drawn inside the next node up.
        string? HostOf(C4Element element)
        {
            var parentId = element.ParentId;
            while (parentId is not null)
            {
                if (byId.TryGetValue(parentId, out C4Element? elementForParentId))
                {
                    return elementForParentId.Id;
                }

                parentId = workspace.Find(parentId)?.ParentId;
            }

            return null;
        }

        var hosts = members.ToDictionary(element => element.Id, HostOf, StringComparer.OrdinalIgnoreCase);
        var hosted = members
            .Where(element => hosts[element.Id] is not null)
            .ToLookup(element => hosts[element.Id]!, StringComparer.OrdinalIgnoreCase);
        var edges = DeploymentRelationshipsOf(workspace, members)
            .Select(relationship => (relationship.SourceId, relationship.DestinationId))
            .ToArray();

        var padding = metrics.BoundaryPadding;
        var labelHeight = metrics.FontSize * metrics.LineHeight;
        var boxes = new Dictionary<string, C4Box>(StringComparer.OrdinalIgnoreCase);
        var boundaries = new List<C4Boundary>();

        // Lays out one set of siblings with its top-left corner at the origin, returning every box
        // and boundary inside it relative to that corner, and the extent it takes up.
        (Dictionary<string, C4Box> Boxes, List<C4Boundary> Boundaries, double Width, double Height) Arrange(IReadOnlyList<C4Element> siblings)
        {
            var sizes = new Dictionary<string, C4Box>(StringComparer.OrdinalIgnoreCase);
            var inner = new Dictionary<string, (Dictionary<string, C4Box> Boxes, List<C4Boundary> Boundaries)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sibling in siblings)
            {
                var children = hosted[sibling.Id].ToArray();
                if (children.Length == 0)
                {
                    sizes[sibling.Id] = MeasureOf(workspace, sibling, metrics);
                    continue;
                }

                (Dictionary<string, C4Box> childBoxes, List<C4Boundary> childBoundaries, double width, double height) = Arrange(children);
                inner[sibling.Id] = (childBoxes, childBoundaries);
                var labelWidth = TextMetric.WidthOf(BoundaryLabelOf(sibling), metrics.FontSize) + 2 * metrics.HorizontalPadding;
                sizes[sibling.Id] = new C4Box(0, 0, Math.Round(Math.Max(width + 2 * padding, labelWidth), 2), Math.Round(height + 2 * padding + labelHeight, 2));
            }

            // Rank the siblings along the relationships between them, each end standing in for
            // the sibling that hosts it.
            var siblingIds = siblings.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? SiblingOf(string id)
            {
                for (var current = id; current is not null; current = hosts.GetValueOrDefault(current))
                {
                    if (siblingIds.Contains(current))
                    {
                        return current;
                    }
                }

                return null;
            }

            var siblingEdges = edges
                .Select(edge => (SourceId: SiblingOf(edge.SourceId), DestinationId: SiblingOf(edge.DestinationId)))
                .Where(edge => edge.SourceId is not null && edge.DestinationId is not null && !string.Equals(edge.SourceId, edge.DestinationId, StringComparison.OrdinalIgnoreCase))
                .Select(edge => (edge.SourceId!, edge.DestinationId!));
            var ranks = Rank(siblings, siblingEdges);

            var placed = Place(siblings, sizes, ranks, direction, rankSeparation, nodeSeparation);
            var left = placed.Values.Min(box => box.X);
            var top = placed.Values.Min(box => box.Y);

            var resultBoxes = new Dictionary<string, C4Box>(StringComparer.OrdinalIgnoreCase);
            var resultBoundaries = new List<C4Boundary>();
            foreach (var sibling in siblings)
            {
                var box = placed[sibling.Id] with { X = Math.Round(placed[sibling.Id].X - left, 2), Y = Math.Round(placed[sibling.Id].Y - top, 2) };
                if (!inner.TryGetValue(sibling.Id, out var contents))
                {
                    resultBoxes[sibling.Id] = box;
                    continue;
                }

                // Outermost first, so a canvas drawing them in order puts the inner ones on top.
                resultBoundaries.Add(new C4Boundary($"boundary:{sibling.Id}", sibling.Name, BoundaryKindOf(sibling), box));
                var dx = box.X + padding;
                var dy = box.Y + padding;
                foreach ((string id, C4Box child) in contents.Boxes)
                {
                    resultBoxes[id] = Offset(child, dx, dy);
                }

                resultBoundaries.AddRange(contents.Boundaries.Select(boundary => boundary with { Box = Offset(boundary.Box, dx, dy) }));
            }

            var extentWidth = placed.Values.Max(box => box.Right) - left;
            var extentHeight = placed.Values.Max(box => box.Bottom) - top;
            return (resultBoxes, resultBoundaries, extentWidth, extentHeight);
        }

        (Dictionary<string, C4Box> topBoxes, List<C4Boundary> topBoundaries, _, _) = Arrange(members.Where(element => hosts[element.Id] is null).ToArray());
        foreach ((string id, C4Box box) in topBoxes)
        {
            boxes[id] = box;
        }

        boundaries.AddRange(topBoundaries);

        var hasAuthored = authored is { Count: > 0 } && authored.Keys.Any(boxes.ContainsKey);
        var authoredIgnored = hasAuthored && view.AutoLayout is not null;
        if (hasAuthored && !authoredIgnored)
        {
            foreach ((string id, C4SidecarPosition position) in authored!)
            {
                if (boxes.TryGetValue(id, out var box) && byId.TryGetValue(id, out var element) && hosts[element.Id] is null)
                {
                    boxes[id] = box with { X = Math.Round(position.X, 2), Y = Math.Round(position.Y, 2) };
                }
            }
        }

        return new C4Layout(boxes, boundaries, authoredIgnored);
    }

    private static C4Box Offset(C4Box box, double dx, double dy) =>
        box with { X = Math.Round(box.X + dx, 2), Y = Math.Round(box.Y + dy, 2) };

    /// <summary>What a deployment node's boundary says after its name, in its brackets.</summary>
    private static string BoundaryKindOf(C4Element element) =>
        element.Technology.Length > 0 ? $"Deployment Node: {element.Technology}" : "Deployment Node";

    private static string BoundaryLabelOf(C4Element element) => $"{element.Name} [{BoundaryKindOf(element)}]";

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

    /// <summary>Longest-path ranks of <paramref name="members"/> over <paramref name="edges"/>; edges with an end outside the members are ignored.</summary>
    private static Dictionary<string, int> Rank(IReadOnlyList<C4Element> members, IEnumerable<(string SourceId, string DestinationId)> edges)
    {
        var ids = members.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
    /// Moves anything *not* inside a boundary out of it, and re-sizes the boundary
    /// afterward. A boundary means "these are the parts of that system", so an external system
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

            boxes[id] = Math.Abs(shortest - left) < Tolerance ? box with { X = Math.Round(box.X - left, 2) }
                : Math.Abs(shortest - right) < Tolerance ? box with { X = Math.Round(box.X + right, 2) }
                : Math.Abs(shortest - up) < Tolerance ? box with { Y = Math.Round(box.Y - up, 2) }
                : box with { Y = Math.Round(box.Y + down, 2) };
            pushedVertically[id] = Math.Abs(shortest - up) < Tolerance || Math.Abs(shortest - down) < Tolerance;
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
    /// <para>
    /// Each one leaves by its own shortest route, knowing nothing of the others, so two that
    /// share an edge arrive at the same place: on the C4 worked example's container view the
    /// customer and the mainframe banking system both left through the top and ended up drawn
    /// one on top of the other.
    /// </para>
    /// <para>
    /// This runs in two stages, and the first is the one that matters. Nudging pairs apart one
    /// at a time - which is all this used to do - cannot separate two boxes that are exactly
    /// coincident: identical geometry means identical arithmetic, so both compute the same
    /// escape and move together, in lockstep, for as many passes as they are given. Coincidence
    /// is not a corner case either. Two elements in different ranks routinely share a position
    /// along their rank, and pushing both out of the same edge sets the other coordinate equal
    /// as well.
    /// </para>
    /// <para>
    /// So the first stage sweeps rather than nudges: everything pushed off the same axis is laid
    /// out in order along the perpendicular one, each element placed after the last. That
    /// terminates in a single pass, cannot leave two members of a group overlapping, and moves
    /// elements only forwards - so the coordinate that carried them clear of the boundary is
    /// never touched and nothing walks back inside. The second stage is the old pairwise nudge,
    /// kept for the collisions a per-axis sweep cannot see: a vertically pushed element against
    /// a horizontally pushed one, or against an outsider that never needed pushing at all.
    /// </para>
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

        // Stage one: everything pushed vertically spreads along X, everything pushed
        // horizontally spreads along Y.
        SweepApart(boxes, pushedVertically.Where(entry => entry.Value).Select(entry => entry.Key), horizontally: true, nodeSeparation);
        SweepApart(boxes, pushedVertically.Where(entry => !entry.Value).Select(entry => entry.Key), horizontally: false, nodeSeparation);

        // Stage two, bounded so a set that cannot be separated cannot spin either.
        for (var pass = 0; pass < outsiders.Count; pass++)
        {
            var moved = false;
            foreach ((string id, bool vertically) in pushedVertically)
            {
                foreach (var other in outsiders)
                {
                    if (string.Equals(other, id, StringComparison.OrdinalIgnoreCase) || !boxes[id].Overlaps(boxes[other]))
                    {
                        continue;
                    }

                    var box = boxes[id];
                    var blocker = boxes[other];

                    // A tie means the two are symmetric about each other, and answering it the
                    // same way for both is how lockstep starts. The ordinally smaller id goes
                    // first, which is arbitrary but never the same answer for both.
                    var goesFirst = string.CompareOrdinal(id, other) < 0;
                    if (vertically)
                    {
                        var toLeft = box.Right - blocker.X + nodeSeparation;
                        var toRight = blocker.Right - box.X + nodeSeparation;
                        boxes[id] = (toLeft < toRight || (Math.Abs(toLeft - toRight) < Tolerance && goesFirst))
                            ? box with { X = Math.Round(box.X - toLeft, 2) }
                            : box with { X = Math.Round(box.X + toRight, 2) };
                    }
                    else
                    {
                        var up = box.Bottom - blocker.Y + nodeSeparation;
                        var down = blocker.Bottom - box.Y + nodeSeparation;
                        boxes[id] = (up < down || (Math.Abs(up - down) < Tolerance && goesFirst))
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

    /// <summary>
    /// Lays <paramref name="ids"/> out in a row (or a column) so no two of them overlap on that
    /// axis - and therefore not at all.
    /// </summary>
    /// <remarks>
    /// Order is by current position and then by id, so the result is what the rank layout already
    /// intended and is the same on every run rather than the order a dictionary happened to
    /// enumerate. Elements only ever move forwards along the axis: an element already clear of
    /// its predecessor is left exactly where it was.
    /// </remarks>
    private static void SweepApart(
        Dictionary<string, C4Box> boxes,
        IEnumerable<string> ids,
        bool horizontally,
        double nodeSeparation)
    {
        var ordered = ids
            .OrderBy(id => horizontally ? boxes[id].X : boxes[id].Y)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToArray();

        double? cursor = null;
        foreach (var id in ordered)
        {
            var box = boxes[id];
            var start = horizontally ? box.X : box.Y;
            var size = horizontally ? box.Width : box.Height;

            if (cursor is { } from && start < from)
            {
                box = horizontally
                    ? box with { X = Math.Round(from, 2) }
                    : box with { Y = Math.Round(from, 2) };
                boxes[id] = box;
                start = from;
            }

            cursor = start + size + nodeSeparation;
        }
    }
}
