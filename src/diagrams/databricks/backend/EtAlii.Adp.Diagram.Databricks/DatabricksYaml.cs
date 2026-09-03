using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The reading helpers the three format parsers share: loading a document's root, walking
/// mappings and sequences, and turning a node into the line range that declares it.
/// </summary>
/// <remarks>
/// YamlDotNet is used here and <b>only</b> for reading. It handles quoting, anchors, aliases and
/// block scalars correctly, gives a line number for every node, and - because JSON is a subset of
/// YAML 1.2 - reads the pipeline settings <c>.json</c> through the same door as the two YAML
/// formats. Nothing is ever serialised back through it: every write is a splice through
/// <see cref="DatabricksDocument"/>.
/// </remarks>
internal static class DatabricksYaml
{
    /// <summary>
    /// The document's root mapping, or null for an empty file or one whose root is not a mapping -
    /// which declares nothing, and is a model with nothing in it rather than an error.
    /// </summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The document is not well-formed YAML.</exception>
    public static YamlMappingNode? Root(DatabricksDocument document)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(document.Text);
        stream.Load(reader);

        return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root
            ? root
            : null;
    }

    /// <summary>The mapping under <paramref name="key"/>, or null where there is none.</summary>
    public static YamlMappingNode? Mapping(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node as YamlMappingNode : null;

    /// <summary>The sequence under <paramref name="key"/>, empty where there is none.</summary>
    public static IEnumerable<YamlNode> Sequence(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode sequence
            ? sequence.Children
            : [];

    /// <summary>The scalar under <paramref name="key"/>, or null where there is none or it is not a scalar.</summary>
    public static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value ?? ""
            : null;

    /// <summary>A boolean scalar read leniently: only a value that says so is true.</summary>
    public static bool Flag(YamlMappingNode mapping, string key) =>
        bool.TryParse(Scalar(mapping, key), out var value) && value;

    /// <summary>
    /// The lines that declare a node, narrowed to the ones that actually say something.
    /// </summary>
    /// <remarks>
    /// A block collection has no closing token, so YamlDotNet gives its mapping an end mark at
    /// the start of whatever follows - which without correction hands every construct a range one
    /// line long, or one that swallows the next construct's first line. Two adjustments fix it:
    /// take the furthest end mark in the subtree rather than the node's own, and step back off a
    /// mark that sits in column one, which is a position after the node rather than within it.
    /// Trailing blank and comment lines are then trimmed, because an edit has no business
    /// rewriting a comment that merely happens to follow a construct.
    /// </remarks>
    public static LineRange Range(YamlNode node, DatabricksDocument document)
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

    /// <summary>
    /// The range that declares a key AND its value - the key scalar's start to the value's end -
    /// which is what removing or replacing a keyed construct has to splice.
    /// </summary>
    public static LineRange Range(YamlNode key, YamlNode value, DatabricksDocument document)
    {
        var keyRange = Range(key, document);
        var valueRange = Range(value, document);
        return new LineRange(
            Math.Min(keyRange.Start, valueRange.Start),
            Math.Max(keyRange.End, valueRange.End));
    }

    /// <summary>The furthest end mark anywhere in a node's subtree.</summary>
    /// <remarks>
    /// An alias resolves to a node YAML requires to have been declared earlier, so following one
    /// can only look backwards and never stretches a range past where the construct really ends.
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
