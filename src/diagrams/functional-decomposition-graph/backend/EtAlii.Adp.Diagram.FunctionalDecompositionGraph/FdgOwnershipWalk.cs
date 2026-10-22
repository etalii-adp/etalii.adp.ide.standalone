using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The ownership loops as the tool has always found them: a walk that visits each element once, from
/// the sources of the ownership connections in document order, and reports a loop where it meets its own
/// path. Of <c>std.acyclic</c>'s findings, it keeps the loops the walk reaches, in the order it reaches them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for the open question on elementary cycles</b> (functional-decomposition-graph-disl-fbl
/// task 7, held to option 1, "Keep today's three", by the Q4 default while the user rules): DISL's
/// <c>diagram.cycles</c> gives every elementary cycle, and the walk misses one that runs through an
/// element it already visited from another loop. Deleted if the user rules for every loop.
/// </para>
/// <para>
/// <b>A self-link is no loop here</b>: it is <c>fdg.self-link</c>'s to report, and the walk leaves it out,
/// where <c>std.acyclic</c> reports it as a cycle of one. A connection whose end names no element
/// contributes the id it is written with, as the tool's walk did; a loop only through such an id is one
/// <c>std.acyclic</c> never reports, so it stays unreported.
/// </para>
/// </remarks>
internal static class FdgOwnershipWalk
{
    private const string OwnershipCycle = "fdg.ownership-cycle";

    /// <summary><paramref name="findings"/> with only the loops the walk reaches, in its order.</summary>
    public static IReadOnlyList<DislFinding> Keep(IReadOnlyList<DislFinding> findings, DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(diagram);

        var walked = Loops(diagram);
        var loops = findings
            .Where(finding => finding.Code == OwnershipCycle)
            .Select(finding => (Finding: finding, At: walked.IndexOf(Key(finding.ElementIds))))
            .Where(loop => loop.At >= 0)
            .OrderBy(loop => loop.At)
            .Select(loop => loop.Finding);
        return [.. findings.Where(finding => finding.Code != OwnershipCycle), .. loops];
    }

    /// <summary>The keys of the loops the walk finds, in the order it finds them.</summary>
    private static List<string> Loops(DislDiagram diagram)
    {
        var edges = diagram.Relations
            .Where(relation => !relation.IsDerived && relation.IsA("Ownership"))
            .Where(relation => !string.IsNullOrEmpty(relation.SourceId) && !string.IsNullOrEmpty(relation.TargetId))
            .Where(relation => !string.Equals(relation.SourceId, relation.TargetId, StringComparison.Ordinal))
            .GroupBy(relation => relation.SourceId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(relation => relation.TargetId!).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> onPath = new(StringComparer.Ordinal);
        List<string> path = [];
        List<string> found = [];
        foreach (var start in edges.Keys)
        {
            Walk(start);
        }

        return found;

        void Walk(string node)
        {
            if (onPath.Contains(node))
            {
                var key = Key(path.GetRange(path.IndexOf(node), path.Count - path.IndexOf(node)));
                if (!found.Contains(key)) found.Add(key);
                return;
            }

            if (!visited.Add(node)) return;
            onPath.Add(node);
            path.Add(node);
            foreach (var child in edges.GetValueOrDefault(node) ?? [])
            {
                Walk(child);
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(node);
        }
    }

    /// <summary>A loop as one text, its members in loop order from the one with the least id.</summary>
    private static string Key(IReadOnlyList<string> members)
    {
        if (members.Count == 0) return "";
        var least = 0;
        for (var i = 1; i < members.Count; i++)
        {
            if (string.CompareOrdinal(members[i], members[least]) < 0) least = i;
        }

        return string.Join('\u0000', members.Skip(least).Concat(members.Take(least)));
    }
}
