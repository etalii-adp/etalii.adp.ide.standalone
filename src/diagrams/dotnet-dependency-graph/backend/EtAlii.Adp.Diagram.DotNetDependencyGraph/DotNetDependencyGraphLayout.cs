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
    public const double LayerWidth = 320;

    /// <summary>Vertical distance between two boxes in one layer.</summary>
    public const double RowHeight = 90;

    /// <summary>How far the package band sits beyond the last project layer.</summary>
    public const double PackageBandGap = 200;

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

        var deepestLayer = 0;
        foreach (var layer in byLayer)
        {
            deepestLayer = Math.Max(deepestLayer, layer.Key);
            var row = 0;
            foreach (var project in layer.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
            {
                positions[project.Id] = (layer.Key * LayerWidth, row * RowHeight);
                row++;
            }
        }

        // Packages, banded beyond the last project layer, ordered by id for the same reason.
        var packageX = (deepestLayer * LayerWidth) + LayerWidth + PackageBandGap;
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
