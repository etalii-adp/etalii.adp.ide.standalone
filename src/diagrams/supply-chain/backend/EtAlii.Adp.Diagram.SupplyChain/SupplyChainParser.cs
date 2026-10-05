using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="SupplyChainModel"/>, recording which lines
/// declare what.
/// </summary>
/// <remarks>
/// <para>
/// <b>YamlDotNet reads and never writes.</b> It gives a line for every node, which is what lets an
/// entry know its own lines; every write is a splice through <see cref="LineSplice"/>, so an
/// unchanged document comes back byte-identical rather than merely equivalent.
/// </para>
/// <para>
/// <b>This parser never throws.</b> A document that is not YAML opens empty and says why; an unknown
/// key, an unknown stage or a number that will not read is kept, passed over and reported. A
/// half-typed document must open and keep every byte its author wrote.
/// </para>
/// </remarks>
public static class SupplyChainParser
{
    internal const string HeaderKey = "supply-chain";
    internal const string GroupsKey = "groups";
    internal const string NodesKey = "nodes";
    internal const string FlowsKey = "flows";

    private static readonly string[] GroupKeys = ["id", "name", "description", "x", "y"];

    private static readonly string[] NodeKeys =
        ["id", "type", "name", "description", "group", "quantity", "unit", "step", "x", "y"];

    private static readonly string[] FlowKeys =
        ["id", "from", "to", "product", "description", "volume", "unit", "step"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static SupplyChainModel Parse(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        YamlStream stream = new();
        try
        {
            using StringReader reader = new(document.Text);
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            var line = Math.Max(0, (int)exception.Start.Line - 1);
            return SupplyChainModel.Empty with { Problems = [new SupplyChainProblem(line, $"The document could not be read as YAML: {exception.Message}")] };
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return SupplyChainModel.Empty;
        }

        List<SupplyChainProblem> problems = [];
        return new SupplyChainModel(
            ReadGroups(root, document, problems),
            ReadNodes(root, document, problems),
            ReadFlows(root, document, problems),
            problems,
            ReadVersion(root, document, problems));
    }

    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<SupplyChainProblem> problems)
    {
        if (Node(root, HeaderKey) is not YamlScalarNode scalar)
        {
            problems.Add(new SupplyChainProblem(0, $"The document does not begin with `{HeaderKey}: {SupplyChainModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new SupplyChainProblem(LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != SupplyChainModel.CurrentVersion)
        {
            problems.Add(new SupplyChainProblem(LineOf(scalar, document), $"This document states version {version}; this module reads version {SupplyChainModel.CurrentVersion}."));
        }

        return version;
    }

    private static List<SupplyChainGroup> ReadGroups(YamlMappingNode root, LineDocument document, List<SupplyChainProblem> problems)
    {
        List<SupplyChainGroup> groups = [];
        foreach (var mapping in Entries(root, GroupsKey, "group", document, problems))
        {
            ReportUnknownKeys(mapping, GroupKeys, document, problems, "group");
            groups.Add(new SupplyChainGroup(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "name") ?? "",
                Scalar(mapping, "description") ?? "",
                Range(mapping, document),
                Number(mapping, "x", document, problems),
                Number(mapping, "y", document, problems)));
        }

        return groups;
    }

    private static List<SupplyChainNode> ReadNodes(YamlMappingNode root, LineDocument document, List<SupplyChainProblem> problems)
    {
        List<SupplyChainNode> nodes = [];
        foreach (var mapping in Entries(root, NodesKey, "node", document, problems))
        {
            ReportUnknownKeys(mapping, NodeKeys, document, problems, "node");

            // A legacy stage word reads as the stage it became; the line keeps its word until edited.
            var type = SupplyChainNodeTypes.Normalize(Scalar(mapping, "type") ?? "");
            if (!SupplyChainNodeTypes.IsKnown(type))
            {
                problems.Add(new SupplyChainProblem(LineOf(mapping, document), $"`{type}` is not a stage this module knows."));
            }

            nodes.Add(new SupplyChainNode(
                Scalar(mapping, "id") ?? "",
                type,
                Scalar(mapping, "name") ?? "",
                Scalar(mapping, "description") ?? "",
                Scalar(mapping, "group") ?? "",
                Number(mapping, "quantity", document, problems),
                Scalar(mapping, "unit") ?? "",
                Number(mapping, "step", document, problems),
                Number(mapping, "x", document, problems),
                Number(mapping, "y", document, problems),
                Range(mapping, document)));
        }

        return nodes;
    }

    private static List<SupplyChainFlow> ReadFlows(YamlMappingNode root, LineDocument document, List<SupplyChainProblem> problems)
    {
        List<SupplyChainFlow> flows = [];
        foreach (var mapping in Entries(root, FlowsKey, "flow", document, problems))
        {
            ReportUnknownKeys(mapping, FlowKeys, document, problems, "flow");
            flows.Add(new SupplyChainFlow(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "from") ?? "",
                Scalar(mapping, "to") ?? "",
                Scalar(mapping, "product") ?? "",
                Scalar(mapping, "description") ?? "",
                Number(mapping, "volume", document, problems),
                Scalar(mapping, "unit") ?? "",
                Number(mapping, "step", document, problems),
                Range(mapping, document)));
        }

        return flows;
    }

    /// <summary>The mappings under a section, reporting every entry that is not one.</summary>
    private static IEnumerable<YamlMappingNode> Entries(
        YamlMappingNode root,
        string key,
        string what,
        LineDocument document,
        List<SupplyChainProblem> problems)
    {
        if (Node(root, key) is not YamlSequenceNode sequence)
        {
            yield break;
        }

        foreach (var node in sequence.Children)
        {
            if (node is YamlMappingNode mapping)
            {
                yield return mapping;
            }
            else
            {
                problems.Add(new SupplyChainProblem(LineOf(node, document), $"A {what} entry is not a mapping and was passed over."));
            }
        }
    }

    private static void ReportUnknownKeys(
        YamlMappingNode mapping,
        string[] known,
        LineDocument document,
        List<SupplyChainProblem> problems,
        string what)
    {
        foreach ((YamlNode key, var _) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name } scalar && !known.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new SupplyChainProblem(LineOf(scalar, document), $"`{name}` is not a key this module reads on a {what}; the line is kept."));
            }
        }
    }

    private static YamlNode? Node(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        Node(mapping, key) is YamlScalarNode scalar ? scalar.Value ?? "" : null;

    /// <summary>
    /// A number from the entry, or <c>null</c> when the key is absent or will not read - the second
    /// of which is reported, because a silently missing value reads as a lost edit.
    /// </summary>
    private static double? Number(YamlMappingNode mapping, string key, LineDocument document, List<SupplyChainProblem> problems)
    {
        if (Node(mapping, key) is not YamlScalarNode scalar)
        {
            return null;
        }

        if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        problems.Add(new SupplyChainProblem(LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a number and was ignored."));
        return null;
    }

    private static int LineOf(YamlNode node, LineDocument document) =>
        Math.Clamp((int)node.Start.Line - 1, 0, Math.Max(0, document.Lines.Count - 1));

    private static LineRange Range(YamlNode node, LineDocument document) => YamlNodeRange.Of(node, document.Lines);
}
