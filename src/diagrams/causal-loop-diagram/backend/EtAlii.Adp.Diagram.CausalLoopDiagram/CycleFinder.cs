namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Finds the elementary cycles of a causal loop diagram - the feedback loops the arrows actually
/// form, as opposed to the ones the document claims.
/// </summary>
/// <remarks>
/// <para>
/// Johnson's elementary cycles algorithm, which is the standard enumerator for this and the one
/// AutoCLD uses for this exact purpose. "Elementary" means no variable repeats within a cycle,
/// which is what a feedback loop is; a walk that visits a variable twice is two loops sharing a
/// path rather than one longer one.
/// </para>
/// <para>
/// <b>The bound is part of the contract rather than a safety valve.</b> The number of elementary
/// cycles in a directed graph can be exponential in its size, so enumeration stops at
/// <see cref="DefaultBound"/> and says so. A check that silently examined fewer cycles than the
/// document contains would report "no unlabelled loops" about a diagram it had not finished
/// reading, which is the failure this whole module exists to avoid in the first place
/// (Requirement 3.5).
/// </para>
/// </remarks>
public static class CycleFinder
{
    /// <summary>
    /// How many elementary cycles are enumerated before the search stops and reports.
    /// </summary>
    /// <remarks>
    /// Chosen to be far above what a readable causal loop diagram contains - a diagram with a
    /// thousand distinct feedback loops has already defeated its reader - while bounding the
    /// exponential case that a dense graph produces.
    /// </remarks>
    private const int DefaultBound = 1000;

    /// <summary>Enumerates the elementary cycles of the model's link graph, in a stable order.</summary>
    public static CycleFinderResult Find(CausalLoopModel model, int bound = DefaultBound)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);

        // The graph as adjacency over variable ids, in document order throughout so the same
        // document always yields the same cycles in the same sequence.
        var order = model.Variables.Select(variable => variable.Id).ToList();
        var known = order.ToHashSet(StringComparer.Ordinal);

        // A link may name a variable the document never declares. Those links are not drawn and
        // are not walked; the validator reports them separately rather than this quietly
        // inventing a node to carry one.
        var adjacency = order.ToDictionary(
            id => id,
            id => model.Links
                .Where(link => string.Equals(link.From, id, StringComparison.Ordinal) && known.Contains(link.To))
                .Select(link => link.To)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            StringComparer.Ordinal);

        var cycles = new List<IReadOnlyList<string>>();
        var truncated = false;

        // Johnson's: for each start vertex in turn, find the cycles whose least vertex - by the
        // document's own ordering - is that one, over the subgraph of vertices at or after it.
        // Restricting the subgraph is what stops each cycle being found once per member.
        for (var start = 0; start < order.Count && !truncated; start++)
        {
            var root = order[start];
            var allowed = order.Skip(start).ToHashSet(StringComparer.Ordinal);
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var blockedOn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var stack = new List<string>();

            Circuit(root, root, allowed, adjacency, blocked, blockedOn, stack, cycles, bound, ref truncated);
        }

        return new CycleFinderResult(cycles, truncated, cycles.Count);
    }

    /// <summary>Johnson's CIRCUIT procedure. Returns whether a cycle was closed through <paramref name="vertex"/>.</summary>
    private static bool Circuit(
        string vertex,
        string root,
        HashSet<string> allowed,
        Dictionary<string, List<string>> adjacency,
        HashSet<string> blocked,
        Dictionary<string, HashSet<string>> blockedOn,
        List<string> stack,
        List<IReadOnlyList<string>> cycles,
        int bound,
        ref bool truncated)
    {
        var found = false;
        stack.Add(vertex);
        blocked.Add(vertex);

        foreach (var next in adjacency[vertex].Where(allowed.Contains))
        {
            if (string.Equals(next, root, StringComparison.Ordinal))
            {
                if (cycles.Count >= bound)
                {
                    truncated = true;
                    break;
                }

                cycles.Add([.. stack]);
                found = true;
            }
            else if (!blocked.Contains(next))
            {
                if (Circuit(next, root, allowed, adjacency, blocked, blockedOn, stack, cycles, bound, ref truncated))
                {
                    found = true;
                }

                if (truncated)
                {
                    break;
                }
            }
        }

        if (found)
        {
            Unblock(vertex, blocked, blockedOn);
        }
        else
        {
            // Nothing closed through here, so remember to unblock this vertex if a neighbour
            // later does - the bookkeeping that keeps Johnson's linear in the cycles it finds
            // rather than exponential in the paths it walks.
            foreach (var next in adjacency[vertex].Where(allowed.Contains))
            {
                if (!blockedOn.TryGetValue(next, out var waiting))
                {
                    waiting = new HashSet<string>(StringComparer.Ordinal);
                    blockedOn[next] = waiting;
                }

                waiting.Add(vertex);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        return found;
    }

    /// <summary>
    /// A directed cycle's canonical form: its members rotated to start at the ordinally least, so
    /// two statements of the same loop that begin at different variables compare equal. The
    /// direction still matters, so this rotates rather than sorts.
    /// </summary>
    /// <remarks>
    /// One reading, shared by the validator (which cycles the arrows form that no statement
    /// claims), by the property grid, and by the writer's auto-claim of loops when a link closes
    /// one - so all three agree on when two cycles are the same loop.
    /// </remarks>
    public static string CanonicalSignature(IReadOnlyList<string> cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (cycle.Count == 0)
        {
            return "";
        }

        var start = 0;
        for (var index = 1; index < cycle.Count; index++)
        {
            if (string.CompareOrdinal(cycle[index], cycle[start]) < 0)
            {
                start = index;
            }
        }

        return string.Join("\0", Enumerable.Range(0, cycle.Count).Select(offset => cycle[(start + offset) % cycle.Count]));
    }

    private static void Unblock(string vertex, HashSet<string> blocked, Dictionary<string, HashSet<string>> blockedOn)
    {
        blocked.Remove(vertex);
        if (!blockedOn.TryGetValue(vertex, out var waiting))
        {
            return;
        }

        // Taken as a snapshot: unblocking a dependant can add to this set while it is walked.
        var pending = waiting.ToList();
        waiting.Clear();

        foreach (var dependant in pending.Where(blocked.Contains))
        {
            Unblock(dependant, blocked, blockedOn);
        }
    }
}
