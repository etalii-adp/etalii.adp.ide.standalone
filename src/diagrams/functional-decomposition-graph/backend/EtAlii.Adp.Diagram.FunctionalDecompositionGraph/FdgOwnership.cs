namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Cycles among the ownership relations. <b>The one cycle implementation.</b>
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
    /// <summary>Every ownership cycle in the model, each as its members in order.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> CyclesIn(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Only the four that own. A connection naming an unknown relation contributes no edge:
        // it is reported as a forbidden link, and guessing which relation the author meant would
        // be inventing ownership the document does not state.
        var edges = model.Connections
            .Where(connection => FdgRelations.ById(connection.Type) is { IsOwnership: true })
            .Where(connection => !string.IsNullOrEmpty(connection.From) && !string.IsNullOrEmpty(connection.To))
            // A SELF-EDGE IS NOT AN OWNERSHIP LOOP HERE, and leaving it in was measured rather
            // than reasoned about: the self-link fixture reported BOTH `fdg.self-link` and an
            // `fdg.ownership-cycle` reading "Ownership loops: u1 -> u1". Two findings for one
            // mistake, and the degenerate one is the less useful - it describes a loop between an
            // element and itself, which is what `fdg.self-link` already says precisely. The
            // specific rule owns it; this one reports loops between DISTINCT elements.
            .Where(connection => !string.Equals(connection.From, connection.To, StringComparison.Ordinal))
            .GroupBy(connection => connection.From, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(connection => connection.To).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> onPath = new(StringComparer.Ordinal);
        List<string> path = [];
        Dictionary<string, IReadOnlyList<string>> found = new(StringComparer.Ordinal);

        foreach (var start in edges.Keys)
        {
            Walk(start);
        }

        return [.. found.Values];

        void Walk(string node)
        {
            if (onPath.Contains(node))
            {
                // The loop is the tail of the current path from where this node first appears.
                var at = path.IndexOf(node);
                if (at >= 0)
                {
                    var cycle = path.GetRange(at, path.Count - at);
                    var key = Normalise(cycle);
                    found.TryAdd(string.Join('\u0000', key), key);
                }

                return;
            }

            if (!visited.Add(node))
            {
                return;
            }

            onPath.Add(node);
            path.Add(node);

            if (edges.TryGetValue(node, out var next))
            {
                foreach (var child in next)
                {
                    Walk(child);
                }
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(node);
        }
    }

    /// <summary>
    /// Whether an ownership link <paramref name="from"/> → <paramref name="to"/> would close a cycle:
    /// whether <paramref name="from"/> is already reachable from <paramref name="to"/> along ownership links.
    /// </summary>
    /// <remarks>
    /// <b>A reachability question, asked here rather than of <see cref="CyclesIn"/>.</b> That function
    /// reports the cycles a document HAS, through a walk that visits each element once; asked of a
    /// document that is already broken, it can report an old loop and never reach the new one. A link
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
