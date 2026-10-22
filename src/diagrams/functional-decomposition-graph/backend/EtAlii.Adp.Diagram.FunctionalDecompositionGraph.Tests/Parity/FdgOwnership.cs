namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// Cycles among the ownership relations. <b>The one cycle implementation.</b>
/// <para>Kept as the oracle the definition's findings and refusals are compared with (functional-decomposition-graph-disl-fbl task 7), verbatim but for <see cref="CyclesIn"/>, which reports every elementary loop since the ruling of 2026-10-22.</para>
/// </summary>
/// <remarks>
/// <para>
/// <b>`shows` is excluded, and that exclusion is the whole point of this file.</b> The four
/// ownership relations - UI child, Action, Data and Function - must form strict trees, so a cycle
/// among them is a breach. `shows` is navigation: an Action may show its own page or any page
/// above it, so <b>a task list, a task detail and back to the task list is a legal diagram</b> and
/// reporting it would be reporting the notation working. A cycle check that includes `shows` is
/// the sabotage this rule was written against, and it fails loudly rather than subtly: the
/// example's own navigation loop is reported.
/// </para>
/// <para>
/// <b>Why a cycle rule exists at all when every ownership relation already allows one parent.</b>
/// The limits alone do not prevent a loop - a Data Element owning another which owns it back has
/// exactly one parent each and breaks no cardinality. It is this rule that makes ownership
/// loop-free; together the two make it a set of strict trees.
/// </para>
/// <para>
/// <b>Reported once per cycle, with its members in order.</b> A three-element loop found from
/// three different starting points is one finding, not three, so each cycle is normalised - rotated
/// so its smallest member id comes first - and de-duplicated on that form.
/// </para>
/// </remarks>
public static class FdgOwnership
{
    /// <summary>
    /// Every elementary ownership cycle in the model, each as its members in order, in the order
    /// <c>diagram.cycles</c> gives them (DISL 12.4): by their member first in document order, then by
    /// member order.
    /// </summary>
    /// <remarks>
    /// <b>Every elementary loop, ruled by the user on 2026-10-22</b> (functional-decomposition-graph-disl-fbl,
    /// "Report all"). Until then this was a walk that visited each element once and reported a loop where
    /// it met its own path, which never reaches a loop through an element it already visited from another:
    /// in <c>ownership-loops-sharing-a-member.fdg</c> it found g1 -> g2 -> g3 -> g1 and missed
    /// g1 -> g4 -> g2 -> g3 -> g1. Only elements are members: an end that names no element is a dangling
    /// reference, and closes no loop.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<string>> CyclesIn(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Each id at the place of its first holder, as the model finds an element.
        Dictionary<string, int> place = new(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            place.TryAdd(element.Id, place.Count);
        }

        // Only the four that own. A connection naming an unknown relation contributes no edge:
        // it is reported as a forbidden link, and guessing which relation the author meant would
        // be inventing ownership the document does not state.
        var edges = model.Connections
            .Where(connection => FdgRelations.ById(connection.Type) is { IsOwnership: true })
            .Where(connection => place.ContainsKey(connection.From) && place.ContainsKey(connection.To))
            // A SELF-EDGE IS NOT AN OWNERSHIP LOOP HERE, and leaving it in was measured rather
            // than reasoned about: the self-link fixture reported BOTH `fdg.self-link` and an
            // `fdg.ownership-cycle` reading "Ownership loops: u1 -> u1". Two findings for one
            // mistake, and the degenerate one is the less useful - it describes a loop between an
            // element and itself, which is what `fdg.self-link` already says precisely. The
            // specific rule owns it; this one reports loops between DISTINCT elements.
            .Where(connection => !string.Equals(connection.From, connection.To, StringComparison.Ordinal))
            .GroupBy(connection => place[connection.From])
            .ToDictionary(group => group.Key, group => group.Select(connection => place[connection.To]).Distinct().ToList());

        // Each elementary cycle once, from its member first in document order: from every start, the
        // paths through later members only that come back to it.
        List<List<int>> found = [];
        List<int> path = [];
        HashSet<int> onPath = [];
        for (var start = 0; start < place.Count; start++)
        {
            Walk(start, start);
        }

        var names = place.OrderBy(entry => entry.Value).Select(entry => entry.Key).ToList();
        return
        [
            .. found
                .OrderBy(cycle => cycle, Comparer<List<int>>.Create(Lexicographic))
                .Select(cycle => Normalise([.. cycle.Select(at => names[at])])),
        ];

        void Walk(int start, int node)
        {
            path.Add(node);
            onPath.Add(node);
            foreach (var child in edges.GetValueOrDefault(node) ?? [])
            {
                if (child == start) found.Add([.. path]);
                else if (child > start && !onPath.Contains(child)) Walk(start, child);
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(node);
        }

        static int Lexicographic(List<int> one, List<int> other)
        {
            for (var i = 0; i < Math.Min(one.Count, other.Count); i++)
            {
                if (one[i] != other[i]) return one[i].CompareTo(other[i]);
            }

            return one.Count.CompareTo(other.Count);
        }
    }

    /// <summary>
    /// Whether an ownership link <paramref name="from"/> → <paramref name="to"/> would close a cycle:
    /// whether <paramref name="from"/> is already reachable from <paramref name="to"/> along ownership links.
    /// </summary>
    /// <remarks>
    /// <b>A reachability question, asked here rather than of <see cref="CyclesIn"/>.</b> That function
    /// reports the cycles a document HAS, and enumerating them is costly where reachability is not. A link
    /// closes a cycle exactly when its target already leads back to its source, and that has one
    /// exact answer. Kept in this file so the ownership rule stays in one place: the same four
    /// relations, `shows` excluded, and self-edges excluded as <see cref="CyclesIn"/> excludes them.
    /// </remarks>
    public static bool WouldClose(FdgModel model, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return false;
        }

        var edges = model.Connections
            .Where(connection => FdgRelations.ById(connection.Type) is { IsOwnership: true })
            .Where(connection => !string.Equals(connection.From, connection.To, StringComparison.Ordinal))
            .GroupBy(connection => connection.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(connection => connection.To).ToList(), StringComparer.Ordinal);

        HashSet<string> seen = new(StringComparer.Ordinal) { to };
        Stack<string> pending = new([to]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (string.Equals(node, from, StringComparison.Ordinal))
            {
                return true;
            }

            if (edges.TryGetValue(node, out var next))
            {
                foreach (var child in next.Where(seen.Add))
                {
                    pending.Push(child);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// One cycle written the same way whichever member it was found from: rotated so its smallest
    /// id leads. Two traversals of one loop then produce one finding rather than two.
    /// </summary>
    private static IReadOnlyList<string> Normalise(IReadOnlyList<string> cycle)
    {
        var lowest = 0;
        for (var i = 1; i < cycle.Count; i++)
        {
            if (string.CompareOrdinal(cycle[i], cycle[lowest]) < 0)
            {
                lowest = i;
            }
        }

        return [.. cycle.Skip(lowest), .. cycle.Take(lowest)];
    }
}
