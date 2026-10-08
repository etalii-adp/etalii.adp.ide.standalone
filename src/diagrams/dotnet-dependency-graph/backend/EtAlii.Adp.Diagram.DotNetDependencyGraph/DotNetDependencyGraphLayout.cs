namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Where the boxes go when nobody has said otherwise: projects layered by dependency depth,
/// packages banded off to one side (Requirement 6.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Computation only.</b> Reading, writing, merging and pruning the stored layout are core's,
/// in <c>RegistrationLayout</c>, and are offered to every module - so this module adds no layout
/// mechanism of its own. If anything here started parsing or persisting a <c>layout:</c> block,
/// it would have gone wrong.
/// </para>
/// <para>
/// <b>Layered by depth, because the graph is directed and the question is "what depends on
/// what".</b> A project that nothing in the solution references sits in the first layer; a
/// project sits one layer beyond the deepest thing it references. Packages are leaves by
/// definition - nothing in the solution depends on a package's own dependencies, because this
/// type reads direct references only - so they band together beyond the last project layer
/// rather than being scattered through it.
/// </para>
/// <para>
/// <b>A cycle cannot hang this.</b> Project references cannot legally form one and MSBuild
/// refuses them, but a diagram derived from files on disk must not deadlock over a file that is
/// wrong: depth is computed with an explicit visiting set, and a node reached again while being
/// visited keeps the depth it already had rather than recursing forever.
/// </para>
/// </remarks>
public static class DotNetDependencyGraphLayout
{
    /// <summary>Horizontal distance between two layers.</summary>
    private const double LayerWidth = 320;

    /// <summary>Vertical distance between two boxes in one layer.</summary>
    private const double RowHeight = 90;

    /// <summary>How far the package band sits beyond the last project layer.</summary>
    private const double PackageBandGap = 200;

    /// <summary>
    /// How many boxes a single layer stacks before it wraps into a second column beside itself.
    /// </summary>
    /// <remarks>
    /// <b>This is the answer to "what does it do when the graph is too large to read", and it
    /// is a measurement rather than a guess.</b> Derived from this repository's own solution
    /// (2026-09-07): 104 projects, 17 packages, 349 edges - and the layer distribution
    /// 1/3/7/4/3/2/4/<b>66</b>/14, because sixty-six diagram modules all reference the same
    /// core and nothing references them, so they share one depth. Stacked, that single layer
    /// was 5,850 units tall against a 3,080-unit-wide diagram: a column no screen shows at a
    /// readable zoom, and the failure mode the requirement names as "an unreadable hairball".
    /// <para>
    /// <b>Wrapping rather than limiting, deliberately.</b> The task offered grouping, filtering
    /// or a stated limit; a limit hides part of the graph, and a derived diagram that silently
    /// shows some of its subject is worse than one that is awkward to read. Wrapping hides
    /// nothing - the same 104 projects are drawn, in a block roughly as wide as it is tall
    /// instead of a ribbon sixty-six deep.
    /// </para>
    /// <para>
    /// Twelve because it keeps the tallest layer near the width of the layers around it at this
    /// repository's shape, and because a column a reader has to scroll past twelve times to
    /// find the next one has stopped being a column.
    /// </para>
    /// </remarks>
    public const int LayerWrapAt = 12;

    /// <summary>How far a wrapped column sits from the one before it, inside one layer.</summary>
    private const double WrapColumnWidth = 260;

    /// <summary>Computes a position for every node of <paramref name="graph"/>.</summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> Compute(DependencyGraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var dependencies = graph.Edges
            .Where(edge => edge.Kind == DependsOnKind.Project)
            .GroupBy(edge => edge.FromElementId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToElementId).ToArray(), StringComparer.Ordinal);

        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var project in graph.Projects)
        {
            DepthOf(project.Id, dependencies, depths, []);
        }

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        // Projects, layered. Ordered by name inside a layer so the arrangement is the same on
        // every run - a computed layout that moved between two identical readings would make a
        // stored position look wrong the first time the diagram was reopened.
        var byLayer = graph.Projects
            .GroupBy(project => depths.GetValueOrDefault(project.Id))
            .OrderBy(group => group.Key);

        // Each layer starts where the previous one ended, so a layer that wrapped into several
        // columns pushes the next one along rather than drawing on top of it.
        var layerX = 0.0;
        var rightmost = 0.0;
        foreach (var layer in byLayer)
        {
            var index = 0;
            foreach (var project in layer.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Wrapped rather than stacked: see LayerWrapAt for the measurement behind this.
                var column = index / LayerWrapAt;
                var row = index % LayerWrapAt;
                var x = layerX + (column * WrapColumnWidth);
                positions[project.Id] = (x, row * RowHeight);
                rightmost = Math.Max(rightmost, x);
                index++;
            }

            var columnsUsed = Math.Max(1, (int)Math.Ceiling(layer.Count() / (double)LayerWrapAt));
            layerX += LayerWidth + ((columnsUsed - 1) * WrapColumnWidth);
        }

        // Packages, banded beyond the last project column, ordered by id for the same reason.
        var packageX = rightmost + LayerWidth + PackageBandGap;
        var packageRow = 0;
        foreach (var package in graph.Packages.OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            positions[package.Id] = (packageX, packageRow * RowHeight);
            packageRow++;
        }

        return positions;
    }

    /// <summary>
    /// One beyond the deepest project this one references. <paramref name="visiting"/> makes a
    /// cycle terminate rather than recurse: the node keeps the depth it has and the walk
    /// unwinds.
    /// </summary>
    private static int DepthOf(
        string id,
        IReadOnlyDictionary<string, string[]> dependencies,
        Dictionary<string, int> depths,
        HashSet<string> visiting)
    {
        if (depths.TryGetValue(id, out var known))
        {
            return known;
        }

        if (!visiting.Add(id))
        {
            // Reached again while still being visited: a cycle. Zero rather than a throw - the
            // diagram is derived from files that may be wrong, and being wrong must not hang it.
            return 0;
        }

        var depth = 0;
        if (dependencies.TryGetValue(id, out var referenced))
        {
            foreach (var target in referenced)
            {
                depth = Math.Max(depth, DepthOf(target, dependencies, depths, visiting) + 1);
            }
        }

        visiting.Remove(id);
        depths[id] = depth;
        return depth;
    }
}
