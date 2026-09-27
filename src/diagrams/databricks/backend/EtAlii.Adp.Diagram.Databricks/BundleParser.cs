using EtAlii.Adp.Documents;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Reads a document as a Databricks Asset Bundle: the <c>databricks.yml</c> reading, recording
/// which lines declare what.
/// </summary>
/// <remarks>
/// Forgiving on purpose. A resource without a kind it knows, a root key it does not model - none
/// of these throw; they land as <see cref="UnknownNode"/>s so the rest of the diagram still draws
/// and the constructs survive by never being touched (Requirement 2.4). Only a document that is
/// not YAML at all fails, and the store carries that as the entry's error.
/// </remarks>
internal static class BundleParser
{
    private static readonly string[] _modelledRootKeys = ["bundle", "include", "variables", "resources", "targets"];
    private static readonly string[] _modelledResourceKinds = ["jobs", "pipelines"];

    public static BundleModel Parse(YamlMappingNode? root, LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (root is null)
        {
            return BundleModel.Empty;
        }

        var bundle = DatabricksYaml.Mapping(root, "bundle");
        var name = bundle is null ? "" : DatabricksYaml.Scalar(bundle, "name") ?? "";

        return new BundleModel(
            name,
            ReadIncludes(root, document),
            ReadVariables(root, document),
            ReadResources(root, document, out var unknownKinds),
            ReadTargets(root, document),
            ReadUnknownRootKeys(root, document).Concat(unknownKinds).ToList());
    }

    private static List<BundleInclude> ReadIncludes(YamlMappingNode root, LineDocument document) =>
        DatabricksYaml.Sequence(root, "include")
            .OfType<YamlScalarNode>()
            .Select(node => new BundleInclude(node.Value ?? "", DatabricksYaml.Range(node, document)))
            .ToList();

    private static List<BundleVariable> ReadVariables(YamlMappingNode root, LineDocument document)
    {
        var variables = new List<BundleVariable>();
        var mapping = DatabricksYaml.Mapping(root, "variables");
        if (mapping is null)
        {
            return variables;
        }

        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode { Value: { } variableName })
            {
                continue;
            }

            // A variable may be declared bare (`name:`) or with a mapping of default/description;
            // a bare declaration is a variable with neither.
            var declaration = value as YamlMappingNode;
            variables.Add(new BundleVariable(
                variableName,
                declaration is null ? "" : DatabricksYaml.Scalar(declaration, "default") ?? "",
                declaration is null ? "" : DatabricksYaml.Scalar(declaration, "description") ?? "",
                DatabricksYaml.Range(key, value, document)));
        }

        return variables;
    }

    private static List<BundleResource> ReadResources(
        YamlMappingNode root, LineDocument document, out List<UnknownNode> unknownKinds)
    {
        var resources = new List<BundleResource>();
        unknownKinds = [];
        var mapping = DatabricksYaml.Mapping(root, "resources");
        if (mapping is null)
        {
            return resources;
        }

        foreach (var (kindNode, entries) in mapping.Children)
        {
            if (kindNode is not YamlScalarNode { Value: { } kind })
            {
                continue;
            }

            if (!_modelledResourceKinds.Contains(kind, StringComparer.Ordinal))
            {
                // An experiment, a model, a schema: real configuration this module does not model.
                // It draws generically and is never written (Requirement 2.4).
                unknownKinds.Add(new UnknownNode(
                    $"resources.{kind}", kind, DatabricksYaml.Range(kindNode, entries, document)));
                continue;
            }

            if (entries is not YamlMappingNode keyed)
            {
                continue;
            }

            foreach (var (keyNode, body) in keyed.Children)
            {
                if (keyNode is YamlScalarNode { Value: { } key })
                {
                    resources.Add(new BundleResource(kind, key, DatabricksYaml.Range(keyNode, body, document)));
                }
            }
        }

        return resources;
    }

    private static List<BundleTarget> ReadTargets(YamlMappingNode root, LineDocument document)
    {
        var targets = new List<BundleTarget>();
        var mapping = DatabricksYaml.Mapping(root, "targets");
        if (mapping is null)
        {
            return targets;
        }

        foreach (var (nameNode, body) in mapping.Children)
        {
            if (nameNode is not YamlScalarNode { Value: { } targetName } || body is not YamlMappingNode target)
            {
                continue;
            }

            targets.Add(new BundleTarget(
                targetName,
                DatabricksYaml.Scalar(target, "mode") ?? "",
                DatabricksYaml.Flag(target, "default"),
                ReadOverrides(target, document),
                DatabricksYaml.Range(nameNode, body, document)));
        }

        return targets;
    }

    /// <summary>
    /// The resources a target overrides: everything keyed under its own <c>resources:</c>, as
    /// kind/key pairs - the override edges of Requirement 3.3.
    /// </summary>
    private static List<BundleResource> ReadOverrides(YamlMappingNode target, LineDocument document)
    {
        var overrides = new List<BundleResource>();
        var mapping = DatabricksYaml.Mapping(target, "resources");
        if (mapping is null)
        {
            return overrides;
        }

        foreach (var (kindNode, entries) in mapping.Children)
        {
            if (kindNode is not YamlScalarNode { Value: { } kind } || entries is not YamlMappingNode keyed)
            {
                continue;
            }

            foreach (var (keyNode, body) in keyed.Children)
            {
                if (keyNode is YamlScalarNode { Value: { } key })
                {
                    overrides.Add(new BundleResource(kind, key, DatabricksYaml.Range(keyNode, body, document)));
                }
            }
        }

        return overrides;
    }

    private static IEnumerable<UnknownNode> ReadUnknownRootKeys(YamlMappingNode root, LineDocument document)
    {
        foreach (var (keyNode, value) in root.Children)
        {
            if (keyNode is YamlScalarNode { Value: { } key }
                && !_modelledRootKeys.Contains(key, StringComparer.Ordinal))
            {
                yield return new UnknownNode(key, key, DatabricksYaml.Range(keyNode, value, document));
            }
        }
    }
}
