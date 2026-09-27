using EtAlii.Adp.Documents;
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
    /// The lines that declare a node: the shared rule (backend-centralization R7, <see cref="YamlNodeRange"/>),
    /// read through this module's own line type until it reads through <see cref="LineDocument"/>.
    /// </summary>
    public static LineRange Range(YamlNode node, DatabricksDocument document) =>
        YamlNodeRange.Of(node, document.Lines, line => line.Text);

    /// <summary>
    /// The range that declares a key AND its value - the key scalar's start to the value's end -
    /// which is what removing or replacing a keyed construct has to splice.
    /// </summary>
    public static LineRange Range(YamlNode key, YamlNode value, DatabricksDocument document) =>
        YamlNodeRange.Of(key, value, document.Lines, line => line.Text);
}
