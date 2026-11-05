namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The knots of a diagram over the relations of one type (DISL §12.4, <c>diagram.knots</c>): its strongly connected
/// components that have more than one member or a self-loop, members in model order, components ordered by their
/// first member.
/// </summary>
/// <remarks>
/// Tarjan's algorithm, iterative so that a deep graph cannot exhaust the stack; the order is imposed afterwards, so
/// the result does not depend on the order the search happens to meet the nodes in.
/// </remarks>
internal static class DislKnots
{
    public static IReadOnlyList<IReadOnlyList<DislElement>> Find(IReadOnlyList<DislElement> nodes, IReadOnlyList<DislElement> relations)
    {
        var position = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < nodes.Count; index++) position[nodes[index]] = index;

        var successors = nodes.Select(_ => new List<int>()).ToArray();
        var selfLoop = new bool[nodes.Count];
        foreach (var relation in relations)
        {
            if (relation is not { Source: { } source, Target: { } target }
                || !position.TryGetValue(source, out var from) || !position.TryGetValue(target, out var to)) continue;
            successors[from].Add(to);
            if (from == to) selfLoop[from] = true;
        }

        var components = Components(successors);
        return
        [
            .. components
                .Where(component => component.Count > 1 || selfLoop[component[0]])
                .Select(component => component.Order().ToList())
                .OrderBy(component => component[0])
                .Select(component => (IReadOnlyList<DislElement>)[.. component.Select(index => nodes[index])]),
        ];
    }

    private static List<List<int>> Components(List<int>[] successors)
    {
        var count = successors.Length;
        var index = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        Array.Fill(index, -1);
        var stack = new Stack<int>();
        var components = new List<List<int>>();
        var counter = 0;

        for (var start = 0; start < count; start++)
        {
            if (index[start] >= 0) continue;
            var work = new Stack<(int Node, int Next)>();
            work.Push((start, 0));
            while (work.Count > 0)
            {
                (int node, int next) = work.Pop();
                if (next == 0)
                {
                    index[node] = low[node] = counter++;
                    stack.Push(node);
                    onStack[node] = true;
                }
                if (next < successors[node].Count)
                {
                    work.Push((node, next + 1));
                    var successor = successors[node][next];
                    if (index[successor] < 0) work.Push((successor, 0));
                    else if (onStack[successor]) low[node] = Math.Min(low[node], index[successor]);
                    continue;
                }
                if (low[node] == index[node])
                {
                    var component = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component.Add(member);
                    }
                    while (member != node);
                    components.Add(component);
                }
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }
            }
        }
        return components;
    }
}
