using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The findings in the order the tool has always given them, where <c>constraints.order</c> - by code,
/// then by line - cannot state it: within <c>fdg.unreadable-entry</c>, across the three link codes, and
/// within the cardinality codes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for gap 8</b> of the companion, as Amendment 1 of functional-decomposition-graph-disl-fbl
/// words it (ruled by the user in chat, 2026-10-11): module code keeps today's order in all three places.
/// </para>
/// <list type="bullet">
/// <item><b>The reader's findings</b> keep the parser's order - per entry its unknown keys, its type, a
/// Comment's name, its numbers; the elements, then the connections, then the header - with the
/// definition's sentence for an unknown type where the parser's stood, and the sizes below their
/// minimum after all of them.</item>
/// <item><b>The link findings</b> are one per connection, in document order, whichever of
/// <c>fdg.dangling-reference</c>, <c>fdg.self-link</c> and <c>fdg.forbidden-link</c> each is.</item>
/// <item><b>The cardinality findings</b> follow the relations in the order the notation lists them,
/// each by the first connection of its group.</item>
/// </list>
/// </remarks>
internal static class FdgFindingOrder
{
    private const string UnreadableEntry = "fdg.unreadable-entry";
    private static readonly string[] _links = ["fdg.dangling-reference", "fdg.self-link", "fdg.forbidden-link"];
    private static readonly string[] _cardinality = ["fdg.second-parent", "fdg.second-shows"];

    /// <summary>The definition's <c>constraints.order</c>.</summary>
    private static readonly Lazy<List<string>> _codes = new(() =>
        FdgDefinition.Specification.Json.TryGetProperty("constraints", out var constraints) && constraints.TryGetProperty("order", out var order)
            ? [.. order.EnumerateArray().Select(code => code.GetString() ?? "")]
            : []);

    /// <summary><paramref name="findings"/>, ordered by <c>constraints.order</c>, in today's order where that order cannot state it.</summary>
    public static IReadOnlyList<DislFinding> Apply(IReadOnlyList<DislFinding> findings, FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(model);

        List<DislFinding> ordered = [];
        foreach (var block in findings.GroupBy(finding => Block(finding.Code)).OrderBy(block => block.Key))
        {
            var code = block.First().Code;
            ordered.AddRange(
                code == UnreadableEntry ? Unreadable([.. block], model)
                : _links.Contains(code) ? block.OrderBy(finding => finding.Line ?? 0)
                : _cardinality.Contains(code) ? block.OrderBy(finding => Relation(finding, model)).ThenBy(finding => FirstLine(finding, model))
                : block);
        }

        return ordered;
    }

    /// <summary>The place of <paramref name="code"/>'s block: the three link codes are one block, as the two cardinality codes are.</summary>
    private static int Block(string code)
    {
        var lead = _links.Contains(code) ? _links[0] : _cardinality.Contains(code) ? _cardinality[0] : code;
        var at = _codes.Value.IndexOf(lead);
        return at < 0 ? int.MaxValue : at;
    }

    /// <summary>The reader's findings in the parser's order, each unknown type's where the parser's sentence stood, then the sizes.</summary>
    private static IEnumerable<DislFinding> Unreadable(List<DislFinding> block, FdgModel model)
    {
        var problems = model.Problems.Select((problem, index) => (Problem: problem, Index: index, IsType: FdgReaderSentences.IsTypeSentence(model, problem))).ToList();
        var handed = problems.Where(entry => !entry.IsType).Select(entry => entry.Index).ToList();
        var types = problems.Where(entry => entry.IsType).ToList();

        var reading = 0;
        var rest = problems.Count;
        return block
            .Select(finding => (Finding: finding, Place: finding.ConstraintId switch
            {
                "std.unreadableEntry" or "std.unparseable" when reading < handed.Count => handed[reading++],
                "unknownElementType" or "unknownRelationType" when types.FirstOrDefault(candidate => candidate.Problem.Line + 1 == finding.Line) is { Problem: not null } type => type.Index,
                _ => rest++,
            }))
            .ToList()
            .OrderBy(entry => entry.Place)
            .Select(entry => entry.Finding);
    }

    /// <summary>The place of a cardinality finding's relation among the notation's relations.</summary>
    private static int Relation(DislFinding finding, FdgModel model)
    {
        var type = finding.ElementIds.Skip(1).Select(model.ConnectionOf).FirstOrDefault(connection => connection is not null)?.Type;
        var at = type is null ? -1 : FdgConnectionTypes.All.ToList().IndexOf(type);
        return at < 0 ? int.MaxValue : at;
    }

    /// <summary>The line of the first connection of a cardinality finding's group.</summary>
    private static int FirstLine(DislFinding finding, FdgModel model) =>
        finding.ElementIds.Skip(1).Select(model.ConnectionOf).OfType<FdgConnection>().Select(connection => connection.Range.Start).DefaultIfEmpty(int.MaxValue).Min();
}
