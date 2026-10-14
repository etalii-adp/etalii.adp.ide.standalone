namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The elementary cycles of a diagram over the relations of one type (DISL §12.4,
/// <c>diagram.cycles</c> and <c>diagram.cyclesTruncated</c>), at most a bound of them.
/// </summary>
/// <remarks>
/// <para>
/// Johnson's elementary cycles algorithm. For each node in model order, the cycles whose member that
/// comes first in model order is that node are found over the nodes at or after it, so each cycle is
/// found once, starting at that member.
/// </para>
/// <para>
/// <b>The order is §12.4's by construction</b>: every node's successors are walked in model order,
/// so the depth-first search closes the cycles of one start in lexicographic order of their members'
/// positions; the start itself, first among the nodes it may reach, closes a shorter cycle before a
/// longer one with the same prefix. The bound therefore keeps the first cycles of that order, and the
/// search stops at the first cycle past it: no more than <c>max</c> are ever kept.
/// </para>
/// </remarks>
internal static class DislCycles
{
    /// <summary>The cycles over <paramref name="relations"/> among <paramref name="nodes"/>, at most <paramref name="max"/>, and whether the bound cut them.</summary>
    public static (IReadOnlyList<IReadOnlyList<DislElement>> Cycles, bool Truncated) Find(IReadOnlyList<DislElement> nodes, IEnumerable<DislElement> relations, long max)
    {
        var position = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < nodes.Count; index++) position[nodes[index]] = index;

        var successors = nodes.Select(_ => new SortedSet<int>()).ToArray();
        foreach (var relation in relations)
        {
            if (relation is { Source: { } source, Target: { } target } && position.TryGetValue(source, out var from) && position.TryGetValue(target, out var to))
            {
                successors[from].Add(to);
            }
        }

        var search = new Search(successors, max);
        for (var start = 0; start < nodes.Count && !search.Truncated; start++) search.From(start);

        return ([.. search.Found.Select(cycle => (IReadOnlyList<DislElement>)[.. cycle.Select(index => nodes[index])])], search.Truncated);
    }

    private sealed class Search(SortedSet<int>[] successors, long max)
    {
        private readonly bool[] _blocked = new bool[successors.Length];
        private readonly HashSet<int>[] _waiting = [.. successors.Select(_ => new HashSet<int>())];
        private readonly List<int> _stack = [];
        private int _root;

        public List<int[]> Found { get; } = [];

        public bool Truncated { get; private set; }

        public void From(int root)
        {
            _root = root;
            for (var index = root; index < successors.Length; index++)
            {
                _blocked[index] = false;
                _waiting[index].Clear();
            }
            Circuit(root);
        }

        /// <summary>Johnson's CIRCUIT: whether a cycle back to the root was closed through <paramref name="vertex"/>.</summary>
        private bool Circuit(int vertex)
        {
            var closed = false;
            _stack.Add(vertex);
            _blocked[vertex] = true;

            foreach (var next in successors[vertex].Where(next => next >= _root))
            {
                if (next == _root)
                {
                    if (Found.Count >= max)
                    {
                        Truncated = true;
                        break;
                    }
                    Found.Add([.. _stack]);
                    closed = true;
                }
                else if (!_blocked[next] && Circuit(next))
                {
                    closed = true;
                }
                if (Truncated) break;
            }

            if (closed)
            {
                Unblock(vertex);
            }
            else
            {
                foreach (var next in successors[vertex].Where(next => next >= _root)) _waiting[next].Add(vertex);
            }

            _stack.RemoveAt(_stack.Count - 1);
            return closed;
        }

        private void Unblock(int vertex)
        {
            _blocked[vertex] = false;
            var waiting = _waiting[vertex].ToList();
            _waiting[vertex].Clear();
            foreach (var dependant in waiting.Where(dependant => _blocked[dependant])) Unblock(dependant);
        }
    }
}
