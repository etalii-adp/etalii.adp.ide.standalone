using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Reads a <see cref="TimelineDocument"/> into a <see cref="TimelineModel"/>, recording which
/// lines declare what.
/// </summary>
/// <remarks>
/// <para>
/// YamlDotNet is used here and <b>only</b> for reading. It handles quoting, anchors, aliases and
/// block scalars correctly, and gives a line number for every node, which is what lets an element
/// know its own lines and a diagnostic point at the one that caused it. Nothing is ever
/// serialised back through it: every write is a splice through <see cref="TimelineDocument"/>.
/// </para>
/// <para>
/// The parser is forgiving on purpose. A time it cannot read, a row it cannot read, a missing id
/// - none of these throw, because Requirement 12.2 says the rest of the diagram still draws and
/// the problem is reported through the panel. Only a document that is not YAML at all fails
/// outright, and that is the validator's one-problem case.
/// </para>
/// </remarks>
public static class TimelineParser
{
    private const string ElementsKey = "elements";
    private const string ConnectionsKey = "connections";

    /// <summary>Reads the document. Throws only when the text is not YAML.</summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The document is not well-formed YAML.</exception>
    public static TimelineModel Parse(TimelineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = new YamlStream();
        using (var reader = new StringReader(document.Text))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            // An empty file, or one whose root is not a mapping, declares no timeline. That is a
            // model with nothing in it rather than an error: the rules decide whether an empty
            // document is worth complaining about, not the parser.
            return TimelineModel.Empty;
        }

        return new TimelineModel(
            ReadElements(root, document),
            ReadConnections(root, document));
    }

    private static List<TimelineElement> ReadElements(YamlMappingNode root, TimelineDocument document)
    {
        var elements = new List<TimelineElement>();
        foreach (var node in Sequence(root, ElementsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            var end = Scalar(mapping, "end");
            elements.Add(new TimelineElement(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "label") ?? "",
                Instant(Scalar(mapping, "begin") ?? ""),
                // A missing key is a moment; a key that is present but unreadable is a period
                // with a broken end, which the rules report. Collapsing the two would make
                // `end:` with nothing after it indistinguishable from no end at all.
                end is null ? null : Instant(end),
                Row(Scalar(mapping, "row")),
                Range(mapping, document)));
        }

        return elements;
    }

    private static List<TimelineConnection> ReadConnections(YamlMappingNode root, TimelineDocument document)
    {
        var connections = new List<TimelineConnection>();
        foreach (var node in Sequence(root, ConnectionsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            connections.Add(new TimelineConnection(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "from") ?? "",
                Scalar(mapping, "to") ?? "",
                Scalar(mapping, "label") ?? "",
                Range(mapping, document)));
        }

        return connections;
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode root, string key) =>
        root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode sequence
            ? sequence.Children
            : [];

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value ?? ""
            : null;

    /// <summary>
    /// Reads a time at face value: no timezone conversion, no calendar, no working days
    /// (Requirement 3.7).
    /// </summary>
    /// <remarks>
    /// The precision is decided by what was written rather than by what the value turns out to
    /// be, so midnight written as a date stays a date. <see cref="DateTimeStyles.AssumeUniversal"/>
    /// with <see cref="DateTimeStyles.AdjustToUniversal"/> pins a value that carries no offset to
    /// offset zero - the alternative, <see cref="DateTimeStyles.None"/>, silently applies the
    /// <b>local machine's</b> offset, so the same document would parse to a different instant in
    /// every timezone. That bug shipped here first and was caught by the scale's round-trip test,
    /// not the parser's own: a test that only inspects date components cannot see an offset.
    /// </remarks>
    private static TimelineInstant Instant(string text)
    {
        var trimmed = text.Trim();
        var precision = trimmed.Contains('T', StringComparison.Ordinal)
            ? TimelinePrecision.DateTime
            : TimelinePrecision.Date;

        var readable = DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value);

        return new TimelineInstant(text, readable ? value : null, precision);
    }

    /// <summary>A row that will not read is row zero, and the rules say so.</summary>
    private static int Row(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) ? row : 0;

    /// <summary>
    /// The lines that declare a node, narrowed to the ones that actually say something.
    /// </summary>
    /// <remarks>
    /// A block collection has no closing token, so YamlDotNet gives its mapping an end mark at
    /// the start of whatever follows - which without correction hands every element a range one
    /// line long, or one that swallows the next element's first line. Two adjustments fix it:
    /// take the furthest end mark in the subtree rather than the node's own, and step back off a
    /// mark that sits in column one, which is a position after the node rather than within it.
    /// Trailing blank and comment lines are then trimmed, because an edit has no business
    /// rewriting a comment that merely happens to follow an element.
    /// </remarks>
    private static LineRange Range(YamlNode node, TimelineDocument document)
    {
        var last = document.Lines.Count - 1;
        var extent = EndMark(node);
        var start = Math.Clamp((int)node.Start.Line - 1, 0, last);
        var end = Math.Clamp((int)extent.Line - 1, start, last);
        if (extent.Column == 1 && end > start)
        {
            end--;
        }

        while (end > start && (document.Lines[end].IsBlank || document.Lines[end].IsComment))
        {
            end--;
        }

        return new LineRange(start, end);
    }

    /// <summary>The furthest end mark anywhere in a node's subtree.</summary>
    /// <remarks>
    /// An alias resolves to a node YAML requires to have been declared earlier, so following one
    /// can only look backwards and never stretches a range past where the element really ends.
    /// </remarks>
    private static YamlDotNet.Core.Mark EndMark(YamlNode node)
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
