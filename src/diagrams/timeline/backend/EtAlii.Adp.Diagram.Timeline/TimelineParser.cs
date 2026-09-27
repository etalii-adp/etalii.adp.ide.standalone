using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="TimelineModel"/>, recording which
/// lines declare what.
/// </summary>
/// <remarks>
/// <para>
/// YamlDotNet is used here and <b>only</b> for reading. It handles quoting, anchors, aliases and
/// block scalars correctly, and gives a line number for every node, which is what lets an element
/// know its own lines and a diagnostic point at the one that caused it. Nothing is ever
/// serialised back through it: every write is a splice through <see cref="LineDocument"/>.
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
    public static TimelineModel Parse(LineDocument document)
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

    private static List<TimelineElement> ReadElements(YamlMappingNode root, LineDocument document)
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

    private static List<TimelineConnection> ReadConnections(YamlMappingNode root, LineDocument document)
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
    /// The lines that declare a node: the shared rule (backend-centralization R7, <see cref="YamlNodeRange"/>).
    /// </summary>
    private static LineRange Range(YamlNode node, LineDocument document) => YamlNodeRange.Of(node, document.Lines);
}
