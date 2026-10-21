using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The boundaries of a view's layout: a deployment view drawn nested, each node a boundary around
/// what it hosts, and the one boundary of a container or component view, kept clear of every
/// element it does not enclose.
/// </summary>
/// <remarks>
/// <see cref="C4LayoutEngine"/> ranks and places; this packs what it placed into boundaries
/// (code-structure T240).
/// </remarks>
internal static class C4BoundaryPacker
{
    private const double Tolerance = 0.000001f;

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
    public static C4Layout ComputeNested(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        C4Metrics metrics,
        IReadOnlyDictionary<string, C4SidecarPosition>? authored,
        string direction,
        int rankSeparation,
        int nodeSeparation)
    {
        var nesting = new Nesting(workspace, members, metrics, direction, rankSeparation, nodeSeparation);

        var boxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var boundaries = new List<C4Boundary>();

        (Dictionary<string, Rect> topBoxes, List<C4Boundary> topBoundaries, _, _) = nesting.Arrange(members.Where(element => nesting.Hosts[element.Id] is null).ToArray());
        foreach ((string id, Rect box) in topBoxes)
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
                if (boxes.TryGetValue(id, out var box) && nesting.ById.TryGetValue(id, out var element) && nesting.Hosts[element.Id] is null)
                {
                    boxes[id] = box with { X = Math.Round(position.X, 2), Y = Math.Round(position.Y, 2) };
                }
            }
        }

        return new C4Layout(boxes, boundaries, authoredIgnored);
    }

    /// <summary>
    /// Moves anything *not* inside a boundary out of it, and re-sizes the boundary
    /// afterward. A boundary means "these are the parts of that system", so an external system
    /// drawn inside one says the opposite of what the diagram means - and the layered layout,
    /// which ranks by relationship distance, has no reason on its own to keep them apart
    /// (found by the manual pass).
    /// </summary>
    public static IReadOnlyList<C4Boundary> PushOutsiders(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        Dictionary<string, Rect> boxes,
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
    public static IReadOnlyList<C4Boundary> BoundariesFor(
        C4Workspace workspace,
        C4View view,
        IReadOnlyList<C4Element> members,
        IReadOnlyDictionary<string, Rect> boxes,
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
        return [new C4Boundary($"{C4Boundary.IdPrefix}{scope.Id}", scope.Name, kind, new Rect(Math.Round(x, 2), Math.Round(y, 2), Math.Round(right - x, 2), Math.Round(bottom - y, 2)))];
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
        Dictionary<string, Rect> boxes,
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

        // Stage two, the shared layout's push-off rule (code-structure T239): each pushed outsider
        // moves off whatever it still overlaps along the axis it was not pushed out on, bounded so a
        // set that cannot be separated cannot spin either. A tie goes to the ordinally smaller id,
        // which is arbitrary but never the same answer for both.
        var index = outsiders.Select((id, position) => (id, position)).ToDictionary(entry => entry.id, entry => entry.position, StringComparer.OrdinalIgnoreCase);
        var arranged = outsiders.Select(id => boxes[id]).ToArray();
        var movers = pushedVertically.Select(entry => (index[entry.Key], entry.Value)).ToArray();
        var tieRank = new int[outsiders.Count];
        foreach ((string id, int rank) in outsiders.Order(StringComparer.Ordinal).Select((id, rank) => (id, rank)))
        {
            tieRank[index[id]] = rank;
        }

        OverlapResolver.PushOffAlongAxes(arranged, movers, tieRank, nodeSeparation, decimals: 2);
        for (var position = 0; position < outsiders.Count; position++)
        {
            boxes[outsiders[position]] = arranged[position];
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
        Dictionary<string, Rect> boxes,
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

    private static Rect Offset(Rect box, double dx, double dy) =>
        box with { X = Math.Round(box.X + dx, 2), Y = Math.Round(box.Y + dy, 2) };

    /// <summary>What a deployment node's boundary says after its name, in its brackets.</summary>
    private static string BoundaryKindOf(C4Element element) =>
        element.Technology.Length > 0 ? $"Deployment Node: {element.Technology}" : "Deployment Node";

    private static string BoundaryLabelOf(C4Element element) => $"{element.Name} [{BoundaryKindOf(element)}]";

    /// <summary>One deployment view's members, with what hosts what among them and the relationships between them.</summary>
    private sealed class Nesting
    {
        private readonly C4Workspace _workspace;
        private readonly C4Metrics _metrics;
        private readonly string _direction;
        private readonly int _rankSeparation;
        private readonly int _nodeSeparation;
        private readonly ILookup<string, C4Element> _hosted;
        private readonly (string SourceId, string DestinationId)[] _edges;

        public Nesting(C4Workspace workspace, IReadOnlyList<C4Element> members, C4Metrics metrics, string direction, int rankSeparation, int nodeSeparation)
        {
            _workspace = workspace;
            _metrics = metrics;
            _direction = direction;
            _rankSeparation = rankSeparation;
            _nodeSeparation = nodeSeparation;

            ById = members.ToDictionary(element => element.Id, StringComparer.OrdinalIgnoreCase);
            Hosts = members.ToDictionary(element => element.Id, HostOf, StringComparer.OrdinalIgnoreCase);
            _hosted = members
                .Where(element => Hosts[element.Id] is not null)
                .ToLookup(element => Hosts[element.Id]!, StringComparer.OrdinalIgnoreCase);
            _edges = C4LayoutEngine.DeploymentRelationshipsOf(workspace, members)
                .Select(relationship => (relationship.SourceId, relationship.DestinationId))
                .ToArray();
        }

        /// <summary>The members by id.</summary>
        public Dictionary<string, C4Element> ById { get; }

        /// <summary>Each member's host on the view, or null for one no node on the view encloses.</summary>
        public Dictionary<string, string?> Hosts { get; }

        /// <summary>
        /// Lays out one set of siblings with its top-left corner at the origin, returning every box
        /// and boundary inside it relative to that corner, and the extent it takes up.
        /// </summary>
        public (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries, double Width, double Height) Arrange(IReadOnlyList<C4Element> siblings)
        {
            (Dictionary<string, Rect> sizes, Dictionary<string, (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries)> inner) = Measure(siblings);

            var ranks = C4LayoutEngine.Rank(siblings, SiblingEdges(siblings));

            var placed = C4LayoutEngine.Place(siblings, sizes, ranks, _direction, _rankSeparation, _nodeSeparation);
            var left = placed.Values.Min(box => box.X);
            var top = placed.Values.Min(box => box.Y);

            (Dictionary<string, Rect> resultBoxes, List<C4Boundary> resultBoundaries) = Gather(siblings, placed, inner, left, top);

            var extentWidth = placed.Values.Max(box => box.Right) - left;
            var extentHeight = placed.Values.Max(box => box.Bottom) - top;
            return (resultBoxes, resultBoundaries, extentWidth, extentHeight);
        }

        /// <summary>
        /// Each sibling's size: a card's own, or a node's boundary around its contents, laid out
        /// first; and those contents, relative to the node.
        /// </summary>
        private (Dictionary<string, Rect> Sizes, Dictionary<string, (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries)> Inner) Measure(IReadOnlyList<C4Element> siblings)
        {
            var padding = _metrics.BoundaryPadding;
            var sizes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            var inner = new Dictionary<string, (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sibling in siblings)
            {
                var children = _hosted[sibling.Id].ToArray();
                if (children.Length == 0)
                {
                    sizes[sibling.Id] = C4LayoutEngine.MeasureOf(_workspace, sibling, _metrics);
                    continue;
                }

                (Dictionary<string, Rect> childBoxes, List<C4Boundary> childBoundaries, double width, double height) = Arrange(children);
                inner[sibling.Id] = (childBoxes, childBoundaries);
                var labelWidth = TextMetric.WidthOf(BoundaryLabelOf(sibling), _metrics.FontSize) + 2 * _metrics.HorizontalPadding;
                sizes[sibling.Id] = new Rect(0, 0, Math.Round(Math.Max(width + 2 * padding, labelWidth), 2), Math.Round(height + 2 * padding + _metrics.FontSize * _metrics.LineHeight, 2));
            }

            return (sizes, inner);
        }

        /// <summary>
        /// The relationships between the siblings, each end standing in for the sibling that hosts it.
        /// </summary>
        private IEnumerable<(string SourceId, string DestinationId)> SiblingEdges(IReadOnlyList<C4Element> siblings)
        {
            var siblingIds = siblings.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _edges
                .Select(edge => (SourceId: SiblingOf(edge.SourceId), DestinationId: SiblingOf(edge.DestinationId)))
                .Where(edge => edge.SourceId is not null && edge.DestinationId is not null && !string.Equals(edge.SourceId, edge.DestinationId, StringComparison.OrdinalIgnoreCase))
                .Select(edge => (edge.SourceId!, edge.DestinationId!));

            string? SiblingOf(string id)
            {
                for (var current = id; current is not null; current = Hosts.GetValueOrDefault(current))
                {
                    if (siblingIds.Contains(current))
                    {
                        return current;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// The siblings' boxes moved to the origin, and each node's contents moved inside its boundary.
        /// </summary>
        private (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries) Gather(
            IReadOnlyList<C4Element> siblings,
            Dictionary<string, Rect> placed,
            Dictionary<string, (Dictionary<string, Rect> Boxes, List<C4Boundary> Boundaries)> inner,
            double left,
            double top)
        {
            var padding = _metrics.BoundaryPadding;
            var resultBoxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
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
                resultBoundaries.Add(new C4Boundary($"{C4Boundary.IdPrefix}{sibling.Id}", sibling.Name, BoundaryKindOf(sibling), box));
                var dx = box.X + padding;
                var dy = box.Y + padding;
                foreach ((string id, Rect child) in contents.Boxes)
                {
                    resultBoxes[id] = Offset(child, dx, dy);
                }

                resultBoundaries.AddRange(contents.Boundaries.Select(boundary => boundary with { Box = Offset(boundary.Box, dx, dy) }));
            }

            return (resultBoxes, resultBoundaries);
        }

        // The nearest ancestor that is on the view: a node the view excludes does not stop what
        // it hosts from being drawn inside the next node up.
        private string? HostOf(C4Element element)
        {
            var parentId = element.ParentId;
            while (parentId is not null)
            {
                if (ById.TryGetValue(parentId, out C4Element? elementForParentId))
                {
                    return elementForParentId.Id;
                }

                parentId = _workspace.Find(parentId)?.ParentId;
            }

            return null;
        }
    }
}
