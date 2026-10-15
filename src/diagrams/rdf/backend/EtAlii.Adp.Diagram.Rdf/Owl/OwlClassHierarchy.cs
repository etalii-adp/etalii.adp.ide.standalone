using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The asserted subclass graph's shape, shared by the layout (column depths) and the validator
/// (cycle warnings): strongly connected components over class-to-superclass adjacency, and the
/// depth of each class with every cycle collapsed to one depth so recursion always terminates.
/// Asserted only - the adjacency comes from <c>rdfs:subClassOf</c> triples, never from entailment.
/// </summary>
internal static class OwlClassHierarchy
{
    /// <summary>
    /// The cycles: every strongly connected component with more than one member, plus every
    /// self-loop - each a set of classes asserting themselves under each other.
    /// </summary>
    public static List<List<string>> Cycles(IReadOnlyDictionary<string, List<string>> supers)
    {
        return Components(supers)
            .Where(component => component.Count > 1
                || (supers.TryGetValue(component[0], out var own) && own.Contains(component[0])))
            .ToList();
    }

    /// <summary>
    /// Column depth per class: a root sits at 0, every other class one past its deepest asserted
    /// superclass, and a cycle's members share one depth (the collapse that keeps this total).
    /// </summary>
    public static Dictionary<string, int> Depths(IReadOnlyDictionary<string, List<string>> supers)
    {
        var components = Components(supers);
        var componentOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < components.Count; i++)
        {
            foreach (var member in components[i])
            {
                componentOf[member] = i;
            }
        }

        // Each component layered as one node, named by its first member: the condensation is
        // acyclic, so the cycle policy never applies.
        var edges = new List<(string From, string To)>();
        foreach ((string child, List<string> parents) in supers)
        {
            foreach (var parent in parents)
            {
                if (componentOf.TryGetValue(parent, out var parentComponent)
                    && parentComponent != componentOf[child])
                {
                    edges.Add((components[parentComponent][0], components[componentOf[child]][0]));
                }
            }
        }

        var componentDepth = Layering.LongestPath(components.Select(component => component[0]), edges, CyclePolicy.BackEdgeIsZero);
        return componentOf.ToDictionary(
            pair => pair.Key, pair => componentDepth[components[pair.Value][0]], StringComparer.Ordinal);
    }

    /// <summary>Tarjan's strongly connected components over the adjacency, deterministic in key order.</summary>
    private static List<List<string>> Components(IReadOnlyDictionary<string, List<string>> supers)
    {
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var components = new List<List<string>>();
        var counter = 0;

        foreach (var node in supers.Keys)
        {
            if (!indexOf.ContainsKey(node))
            {
                Connect(node);
            }
        }

        return components;

        void Connect(string node)
        {
            indexOf[node] = counter;
            lowOf[node] = counter;
            counter++;
            stack.Push(node);
            onStack.Add(node);

            if (supers.TryGetValue(node, out var parents))
            {
                foreach (var parent in parents)
                {
                    if (!supers.ContainsKey(parent) && !indexOf.ContainsKey(parent))
                    {
                        continue; // a superclass with no own subclass entry cannot be in a cycle
                    }

                    if (!indexOf.TryGetValue(parent, out var parentIndex))
                    {
                        Connect(parent);
                        lowOf[node] = Math.Min(lowOf[node], lowOf[parent]);
                    }
                    else if (onStack.Contains(parent))
                    {
                        lowOf[node] = Math.Min(lowOf[node], parentIndex);
                    }
                }
            }

            if (lowOf[node] == indexOf[node])
            {
                var component = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (member != node);

                component.Reverse();
                components.Add(component);
            }
        }
    }
}
