using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Documents;

/// <summary>
/// The lines a parsed YAML node occupies, narrowed to the ones that actually say something - written
/// once for every module that edits YAML by splicing lines (backend-centralization R7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a node's own end mark is not its end.</b> A block collection has no closing token, so
/// YamlDotNet gives its mapping an end mark at the start of whatever follows. Uncorrected, that hands
/// every element a range one line long, or one that swallows the next element's first line. Two
/// adjustments fix it. First, take the furthest end mark in the node's SUBTREE rather than the node's
/// own. Second, step back off a mark that sits in column one, which is a position after the node
/// rather than within it; for a block scalar that is the start of the next line.
/// </para>
/// <para>
/// <b>Trailing blank and comment lines are then trimmed</b>, because an edit has no business rewriting
/// a comment that merely happens to follow an element. Without that trim, a stage's range would take
/// in the comment introducing the stage after it, and a splice would eat it.
/// </para>
/// <para>
/// <b>What a comment is comes from <see cref="Line.IsComment"/>, and that is the one real difference
/// the four copies this replaces had.</b> Their range bodies were identical. databricks, dependency-graph
/// and timeline trim leading whitespace of every kind before looking for <c>#</c>, while azure-pipeline's
/// own line type trimmed spaces only. So a comment line indented with a TAB was trimmed from a range by
/// three modules and kept inside it by the fourth - where a splice would then eat it. The three win
/// (R7.2): a tab-led comment is a comment. Whether YamlDotNet accepts a tab-led comment line in block
/// context, and so whether the difference could ever be reached end to end, was not measured when
/// this was written.
/// </para>
/// <para>
/// An alias resolves to the node it points at, which YAML requires to have been declared earlier in
/// the file. So following one can only ever look backwards, and never stretches a range past where the
/// element actually ends.
/// </para>
/// </remarks>
public static class YamlNodeRange
{
    /// <summary>The lines <paramref name="node"/> occupies in <paramref name="lines"/>.</summary>
    /// <param name="node">A node parsed from the text <paramref name="lines"/> hold.</param>
    /// <param name="lines">The document's lines, in order - <c>LineDocument.Lines</c> for most modules.</param>
    public static LineRange Of(YamlNode node, IReadOnlyList<Line> lines)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(lines);

        var last = lines.Count - 1;
        var extent = EndMark(node);

        // YamlDotNet counts in long; a document with more lines than an int holds is not a thing.
        var start = Math.Clamp((int)node.Start.Line - 1, 0, last);
        var end = Math.Clamp((int)extent.Line - 1, start, last);
        if (extent.Column == 1 && end > start)
        {
            end--;
        }

        while (end > start && (lines[end].IsBlank || lines[end].IsComment))
        {
            end--;
        }

        return new LineRange(start, end);
    }

    /// <summary>
    /// The lines a mapping entry occupies - its key and its value together, which is what removing or
    /// moving the entry has to take (databricks's form).
    /// </summary>
    public static LineRange Of(YamlNode key, YamlNode value, IReadOnlyList<Line> lines)
    {
        var keyRange = Of(key, lines);
        var valueRange = Of(value, lines);
        return new LineRange(
            Math.Min(keyRange.Start, valueRange.Start),
            Math.Max(keyRange.End, valueRange.End));
    }

    // The furthest end mark in a node's subtree.
    private static Mark EndMark(YamlNode node)
    {
        var end = node.End;
        foreach (var child in Descend(node))
        {
            var childEnd = EndMark(child);
            if (childEnd.Line > end.Line)
            {
                end = childEnd;
            }
        }

        return end;
    }

    private static IEnumerable<YamlNode> Descend(YamlNode node) => node switch
    {
        YamlMappingNode mapping => mapping.Children.Keys.Concat(mapping.Children.Values),
        YamlSequenceNode sequence => sequence.Children,
        _ => [],
    };
}
