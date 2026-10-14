namespace EtAlii.Adp.Layout;

/// <summary>Layers of a directed graph, for the layouts that draw one column per layer.</summary>
public static class Layering
{
    /// <summary>
    /// Every node's layer: the length of the longest path of edges leading into it, so every edge
    /// points to a later layer and a node with nothing before it sits in layer zero.
    /// </summary>
    /// <param name="nodes">The nodes, in the order a tie is resolved: earlier nodes are walked first.</param>
    /// <param name="edges">The edges, from the node that comes first to the node that waits for it. An edge naming a node outside <paramref name="nodes" /> is ignored.</param>
    /// <param name="policy">What an edge closing a cycle does.</param>
    /// <returns>The layer of each node, keyed by the node with ordinal comparison.</returns>
    public static Dictionary<string, int> LongestPath(
        IEnumerable<string> nodes, IEnumerable<(string From, string To)> edges, CyclePolicy policy)
    {
        var order = nodes.ToList();
        var before = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in order)
        {
            before.TryAdd(node, []);
        }

        foreach ((string from, string to) in edges)
        {
            if (before.ContainsKey(from) && before.TryGetValue(to, out var dependencies))
            {
                dependencies.Add(from);
            }
        }

        return policy switch
        {
            CyclePolicy.BackEdgeIsZero => DepthFirst(order, before, backEdge: 0),
            CyclePolicy.BackEdgeIgnored => DepthFirst(order, before, backEdge: -1),
            CyclePolicy.BackEdgeIsZeroPerNode => order.Distinct(StringComparer.Ordinal).ToDictionary(
                node => node, node => Unremembered(node, before, []), StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
        };
    }

    /// <summary>
    /// A depth-first walk that remembers nothing, so a node reached along two paths is measured
    /// twice, each time against the stack of the path that reached it.
    /// </summary>
    private static int Unremembered(string node, Dictionary<string, List<string>> before, HashSet<string> resolving)
    {
        if (!resolving.Add(node))
        {
            return 0;
        }

        var depth = 0;
        foreach (var dependency in before[node])
        {
            depth = Math.Max(depth, Unremembered(dependency, before, resolving) + 1);
        }

        resolving.Remove(node);
        return depth;
    }

    /// <summary>
    /// A memoised depth-first walk; <paramref name="backEdge" /> is the depth a node already on
    /// the stack counts as, so 0 lets the edge into it push one layer and -1 lets it push none.
    /// </summary>
    private static Dictionary<string, int> DepthFirst(
        List<string> order, Dictionary<string, List<string>> before, int backEdge)
    {
        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in order)
        {
            Depth(node);
        }

        return layers;

        int Depth(string node)
        {
            if (layers.TryGetValue(node, out var known))
            {
                return known;
            }

            if (!resolving.Add(node))
            {
                return backEdge;
            }

            var depth = 0;
            foreach (var dependency in before[node])
            {
                depth = Math.Max(depth, Depth(dependency) + 1);
            }

            resolving.Remove(node);
            layers[node] = depth;
            return depth;
        }
    }
}
