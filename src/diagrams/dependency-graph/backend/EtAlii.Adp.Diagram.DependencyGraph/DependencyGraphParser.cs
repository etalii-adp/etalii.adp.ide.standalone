using System.Globalization;
using EtAlii.Adp.Documents;
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
                YamlNodeRange.Of(mapping, document.Lines)));
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
                YamlNodeRange.Of(mapping, document.Lines)));
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

}
