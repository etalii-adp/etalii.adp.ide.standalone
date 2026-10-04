using EtAlii.Adp.Documents;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Reads every Lakeflow declarative pipeline a document declares - keyed under
/// <c>resources: pipelines:</c> in a bundle or resource file, or the whole document for a bare
/// pipeline settings <c>.json</c> - recording which lines declare what (Requirement 5).
/// </summary>
/// <remarks>
/// One parser for both shapes because they are the same object at different depths: the settings
/// file's root mapping carries exactly the keys a keyed declaration's body does. JSON arrives
/// here through the same YAML reading path as everything else - JSON is a subset of YAML 1.2.
/// </remarks>
internal static class PipelineParser
{
    private static readonly string[] _modelledKeys =
    [
        "name", "catalog", "schema", "target", "serverless", "continuous", "development",
        "channel", "libraries", "notifications",
    ];

    public static IReadOnlyList<PipelineModel> Parse(YamlMappingNode? root, LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pipelines = new List<PipelineModel>();
        if (root is null)
        {
            return pipelines;
        }

        if (DatabricksYaml.Mapping(root, "resources") is { } resources
            && DatabricksYaml.Mapping(resources, "pipelines") is { } keyed)
        {
            foreach ((YamlNode keyNode, YamlNode body) in keyed.Children)
            {
                if (keyNode is YamlScalarNode { Value: { } key } && body is YamlMappingNode pipeline)
                {
                    pipelines.Add(Read(key, pipeline, DatabricksYaml.Range(keyNode, body, document), document));
                }
            }

            return pipelines;
        }

        if (DatabricksYaml.Scalar(root, "name") is { Length: > 0 } || root.Children.ContainsKey(new YamlScalarNode("libraries")))
        {
            // A bare settings file: the root mapping IS the pipeline. Recognised by carrying a
            // pipeline's own keys rather than by extension, so a .yml settings file reads too.
            pipelines.Add(Read("", root, DatabricksYaml.Range(root, document), document));
        }

        return pipelines;
    }

    private static PipelineModel Read(
        string key, YamlMappingNode pipeline, LineRange lines, LineDocument document) =>
        new(
            key,
            DatabricksYaml.Scalar(pipeline, "name") ?? "",
            DatabricksYaml.Scalar(pipeline, "catalog") ?? "",
            // `schema` succeeded `target` for the same idea; a file says one or the other.
            DatabricksYaml.Scalar(pipeline, "schema") ?? DatabricksYaml.Scalar(pipeline, "target") ?? "",
            DatabricksYaml.Flag(pipeline, "serverless"),
            DatabricksYaml.Flag(pipeline, "continuous"),
            DatabricksYaml.Flag(pipeline, "development"),
            DatabricksYaml.Scalar(pipeline, "channel") ?? "",
            ReadLibraries(pipeline, document),
            ReadNotifications(pipeline, document),
            ReadUnknownKeys(pipeline, document),
            lines);

    private static List<PipelineLibrary> ReadLibraries(YamlMappingNode pipeline, LineDocument document)
    {
        var libraries = new List<PipelineLibrary>();
        foreach (var node in DatabricksYaml.Sequence(pipeline, "libraries"))
        {
            if (node is not YamlMappingNode entry)
            {
                continue;
            }

            // A library entry is a single-key mapping naming its kind; the path field inside
            // depends on the kind (`path` for notebook and file, `include` for glob).
            (string kind, string path) = entry.Children.Count == 1
                                         && entry.Children.First() is { Key: YamlScalarNode { Value: { } kindName }, Value: YamlMappingNode inner }
                    ? (kindName, DatabricksYaml.Scalar(inner, "path") ?? DatabricksYaml.Scalar(inner, "include") ?? "")
                    : ("other", "");

            var modelled = kind is "notebook" or "file" or "glob";
            libraries.Add(new PipelineLibrary(
                modelled ? kind : "other", path, DatabricksYaml.Range(entry, document)));
        }

        return libraries;
    }

    private static List<PipelineNotification> ReadNotifications(YamlMappingNode pipeline, LineDocument document)
    {
        var notifications = new List<PipelineNotification>();
        foreach (var node in DatabricksYaml.Sequence(pipeline, "notifications"))
        {
            if (node is not YamlMappingNode entry)
            {
                continue;
            }

            notifications.Add(new PipelineNotification(
                Strings(entry, "email_recipients"),
                Strings(entry, "alerts"),
                DatabricksYaml.Range(entry, document)));
        }

        return notifications;
    }

    private static List<string> Strings(YamlMappingNode mapping, string key) =>
        DatabricksYaml.Sequence(mapping, key)
            .OfType<YamlScalarNode>()
            .Select(node => node.Value ?? "")
            .ToList();

    private static List<UnknownNode> ReadUnknownKeys(YamlMappingNode pipeline, LineDocument document)
    {
        var unknown = new List<UnknownNode>();
        foreach ((YamlNode keyNode, YamlNode value) in pipeline.Children)
        {
            if (keyNode is YamlScalarNode { Value: { } key }
                && !_modelledKeys.Contains(key, StringComparer.Ordinal))
            {
                unknown.Add(new UnknownNode(key, key, DatabricksYaml.Range(keyNode, value, document)));
            }
        }

        return unknown;
    }
}
