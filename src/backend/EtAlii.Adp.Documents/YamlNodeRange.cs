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
/// <b>What a comment is, and the one real difference the four copies this replaces had.</b> Their
/// range bodies were identical; what differed was the line type's comment test. databricks,
/// dependency-graph and timeline trimmed leading whitespace of every kind before looking for
/// <c>#</c>, while azure-pipeline's own line type trimmed spaces only. So a line whose first
/// non-space character is a TAB followed by <c>#</c> was trimmed from a range by three modules and
/// kept by the fourth. <b>azure-pipeline's rule is kept</b>, under the design's S7 rule that the
/// three win unless azure-pipeline's variant is shown to handle an input they get wrong - and it
/// was shown, by measurement against YamlDotNet on 2026-09-27. YamlDotNet refuses most tab-led
/// comment lines in block context outright ("found invalid tab as indentation"), the ones it accepts
/// lie past the end mark of the last token before them, and a flow node's end mark sits on the line
/// of its own last token - so a genuine tab-led comment never reaches a range's end. The only way
/// such a line does is as the last CONTENT line of a block
/// scalar: of 695 generated inputs YamlDotNet accepted, the two rules disagreed on 127, and in every
/// one the disputed line was scalar content, which the three modules' rule cut out of the element
/// that holds it. YAML indents with spaces alone, so a line is a trailing comment when its first
/// non-space character is <c>#</c> - which is why this does not use <see cref="Line.IsComment"/>,
/// whose general rule causal-loop's own format reads.
/// </para>
/// <para>
/// <b>What this still gets wrong</b>, in all four copies alike and so not R7's to change: a block
/// scalar whose last content line starts, after its spaces, with <c>#</c> has that line trimmed as
/// though it were a comment.
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
    public static LineRange Of(YamlNode node, IReadOnlyList<Line> lines) => Of(node, lines, LineText);

    /// <summary>
    /// The lines a mapping entry occupies - its key and its value together, which is what removing or
    /// moving the entry has to take (databricks's form).
    /// </summary>
    public static LineRange Of(YamlNode key, YamlNode value, IReadOnlyList<Line> lines) =>
        Of(key, value, lines, LineText);

    /// <summary>
    /// The lines <paramref name="node"/> occupies, for a module that still holds its lines in a type of
    /// its own - databricks and azure-pipeline, until they read through <see cref="LineDocument"/>.
    /// </summary>
    /// <param name="node">A node parsed from the text <paramref name="lines"/> hold.</param>
    /// <param name="lines">The document's lines, in order.</param>
    /// <param name="text">A line's text without its terminator. Only the text is read, so the
    /// blank-and-comment rule is this type's, whatever the line type's own tests say.</param>
    public static LineRange Of<TLine>(YamlNode node, IReadOnlyList<TLine> lines, Func<TLine, string> text)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(text);

        var last = lines.Count - 1;
        var extent = EndMark(node);

        // YamlDotNet counts in long; a document with more lines than an int holds is not a thing.
        var start = Math.Clamp((int)node.Start.Line - 1, 0, last);
        var end = Math.Clamp((int)extent.Line - 1, start, last);
        if (extent.Column == 1 && end > start)
        {
            end--;
        }

        while (end > start && SaysNothing(text(lines[end])))
        {
            end--;
        }

        return new LineRange(start, end);
    }

    /// <summary>
    /// The lines a mapping entry occupies, for a module that still holds its lines in a type of its own.
    /// </summary>
    public static LineRange Of<TLine>(YamlNode key, YamlNode value, IReadOnlyList<TLine> lines, Func<TLine, string> text)
    {
        var keyRange = Of(key, lines, text);
        var valueRange = Of(value, lines, text);
        return new LineRange(
            Math.Min(keyRange.Start, valueRange.Start),
            Math.Max(keyRange.End, valueRange.End));
    }

    // A blank line, or a YAML comment line: the first non-SPACE character is '#'. Spaces only,
    // because YAML indents with nothing else - see the remarks for the measurement behind it.
    private static bool SaysNothing(string text) =>
        string.IsNullOrWhiteSpace(text) || text.TrimStart(' ').StartsWith('#');

    private static string LineText(Line line) => line.Text;

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
