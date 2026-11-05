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
/// <item><b>The reader's findings</b> keep the order the tool has always given them (<see cref="FdgReaderSentences"/>) - per entry its unknown keys, its type, a
/// Comment's name, its numbers; the elements, then the connections, then the header - with the
/// definition's sentence for an unknown type where the reader's mark stands, and the sizes below their
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

    /// <summary>
    /// <paramref name="findings"/>, ordered by <c>constraints.order</c>, in today's order where that order cannot
    /// state it: the reader's by <paramref name="sentences"/>, the connections' by <paramref name="diagram"/>.
    /// </summary>
    public static IReadOnlyList<DislFinding> Apply(IReadOnlyList<DislFinding> findings, IReadOnlyList<FdgReaderSentence> sentences, DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(diagram);

        List<DislFinding> ordered = [];
        foreach (var block in findings.GroupBy(finding => Block(finding.Code)).OrderBy(block => block.Key))
        {
            var code = block.First().Code;
            ordered.AddRange(
                code == UnreadableEntry ? Unreadable([.. block], sentences)
                : _links.Contains(code) ? block.OrderBy(finding => finding.Line ?? 0)
                : _cardinality.Contains(code) ? block.OrderBy(finding => Relation(finding, diagram)).ThenBy(finding => FirstLine(finding, diagram))
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

    /// <summary>The reader's findings in the reader's order, each unknown type's where its mark stands, then the sizes.</summary>
    private static IEnumerable<DislFinding> Unreadable(List<DislFinding> block, IReadOnlyList<FdgReaderSentence> sentences)
    {
        var marked = sentences.Select((sentence, index) => (Sentence: sentence, Index: index)).ToList();
        var handed = marked.Where(entry => !entry.Sentence.IsType).Select(entry => entry.Index).ToList();
        var types = marked.Where(entry => entry.Sentence.IsType).ToList();

        var reading = 0;
        var rest = sentences.Count;
        return block
            .Select(finding => (Finding: finding, Place: finding.ConstraintId switch
            {
                "std.unreadableEntry" or "std.unparseable" when reading < handed.Count => handed[reading++],
                "unknownElementType" or "unknownRelationType" when types.FirstOrDefault(candidate => candidate.Sentence.Line + 1 == finding.Line) is { Sentence: not null } type => type.Index,
                _ => rest++,
            }))
            .ToList()
            .OrderBy(entry => entry.Place)
            .Select(entry => entry.Finding);
    }

    /// <summary>The connection <paramref name="id"/> names: the first written with it.</summary>
    private static DislElement? ConnectionOf(DislDiagram diagram, string id) =>
        diagram.Elements.FirstOrDefault(element => element.Type.IsRelation && FdgDefinition.WrittenId(element) == id);

    /// <summary>The place of a cardinality finding's relation among the notation's relations.</summary>
    private static int Relation(DislFinding finding, DislDiagram diagram)
    {
        var type = finding.ElementIds.Skip(1).Select(id => ConnectionOf(diagram, id)).FirstOrDefault(connection => connection is not null)?.Type.Name;
        var at = type is null ? -1 : FdgReading.RelationTypes.ToList().IndexOf(type);
        return at < 0 ? int.MaxValue : at;
    }

    /// <summary>The line of the first connection of a cardinality finding's group.</summary>
    private static int FirstLine(DislFinding finding, DislDiagram diagram) =>
        finding.ElementIds.Skip(1).Select(id => ConnectionOf(diagram, id)?.Line).OfType<int>().DefaultIfEmpty(int.MaxValue).Min();
}
