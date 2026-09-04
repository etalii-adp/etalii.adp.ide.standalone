using System.Globalization;
using EtAlii.Adp.Backend.Hierarchy;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="DependencyGraphModel"/>,
/// recording which lines declare what.
/// </summary>
/// <remarks>
/// <para>
/// YamlDotNet is used here and <b>only</b> for reading. It handles quoting, anchors, aliases and
/// block scalars correctly, and gives a line number for every node, which is what lets an element
/// know its own lines and a diagnostic point at the one that caused it. Nothing is ever
/// serialised back through it: every write is a splice through
/// <see cref="LineDocument"/>.
/// </para>
/// <para>
/// The parser is forgiving on purpose. A coordinate it cannot read, a row it cannot read, a
/// missing id - none of these throw, because the rest of the diagram still draws and the problem
/// is reported through the panel. Only a document that is not YAML at all fails outright, and
/// that is the validator's one-problem case.
/// </para>
/// <para>
/// There is nothing here that reads a time. The timeline's <c>Instant</c> reader, with its
/// offset-zero rule and its precision, went with the dates it existed for.
/// </para>
/// </remarks>
public static class DependencyGraphParser
{
    private const string ElementsKey = "elements";
    private const string RelationsKey = "relations";

    /// <summary>Reads the document. Throws only when the text is not YAML.</summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The document is not well-formed YAML.</exception>
    public static DependencyGraphModel Parse(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = new YamlStream();
        using (var reader = new StringReader(document.Text))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            // An empty file, or one whose root is not a mapping, declares no graph. That is a
            // model with nothing in it rather than an error: the rules decide whether an empty
            // document is worth complaining about, not the parser.
            return DependencyGraphModel.Empty;
        }

        return new DependencyGraphModel(
            ReadElements(root, document),
            ReadRelations(root, document));
    }

    private static List<DependencyGraphElement> ReadElements(YamlMappingNode root, LineDocument document)
    {
        var elements = new List<DependencyGraphElement>();
        foreach (var node in Sequence(root, ElementsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            elements.Add(new DependencyGraphElement(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "label") ?? "",
                X(Scalar(mapping, "x")),
                Row(Scalar(mapping, "row")),
                Range(mapping, document)));
        }

        return elements;
    }

    private static List<DependencyGraphRelation> ReadRelations(YamlMappingNode root, LineDocument document)
    {
        var relations = new List<DependencyGraphRelation>();
        foreach (var node in Sequence(root, RelationsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            relations.Add(new DependencyGraphRelation(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "from") ?? "",
                Scalar(mapping, "to") ?? "",
                Scalar(mapping, "label") ?? "",
                Range(mapping, document)));
        }

        return relations;
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
    /// A horizontal coordinate that will not read is coordinate zero, and the rules say so.
    /// </summary>
    /// <remarks>
    /// Invariant culture, so a document written on a machine with a comma decimal separator says
    /// the same thing everywhere. The value is a plain canvas number and never a time, which is
    /// why this is four lines rather than the timeline's offset-zero instant reader.
    /// </remarks>
    private static double X(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0d;

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
    private static LineRange Range(YamlNode node, LineDocument document)
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
