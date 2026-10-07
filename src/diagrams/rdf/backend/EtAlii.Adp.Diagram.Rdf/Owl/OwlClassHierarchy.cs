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

        // Component adjacency, then depth by memoized walk - acyclic by construction.
        var componentSupers = new List<HashSet<int>>();
        for (var i = 0; i < components.Count; i++)
        {
            componentSupers.Add([]);
        }

        foreach ((string child, List<string> parents) in supers)
        {
            foreach (var parent in parents)
            {
                if (componentOf.TryGetValue(parent, out var parentComponent)
                    && parentComponent != componentOf[child])
                {
                    componentSupers[componentOf[child]].Add(parentComponent);
                }
            }
        }

        var componentDepth = new int[components.Count];
        Array.Fill(componentDepth, -1);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string member, int component) in componentOf)
        {
            depths[member] = DepthOf(component);
        }

        return depths;

        int DepthOf(int component)
        {
            if (componentDepth[component] >= 0)
            {
                return componentDepth[component];
            }

            componentDepth[component] = 0; // settles self-references during the walk
            var depth = 0;
            foreach (var parent in componentSupers[component])
            {
                depth = Math.Max(depth, DepthOf(parent) + 1);
            }

            componentDepth[component] = depth;
            return depth;
        }
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
