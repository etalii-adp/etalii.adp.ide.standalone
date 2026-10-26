using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="SupplyChainModel"/>, recording which lines
/// declare what: the module's own reader until supply-chain-disl-fbl task 10, kept beside the hand-written
/// oracle so that every document of the corpus is seen to read through the binding as it read here.
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
internal static class SupplyChainParser
{
    private const string HeaderKey = "supply-chain";
    private const string GroupsKey = "groups";
    private const string NodesKey = "nodes";
    private const string FlowsKey = "flows";

    private static readonly string[] GroupKeys = ["id", "name", "description", "x", "y"];

    private static readonly string[] NodeKeys =
        ["id", "type", "name", "description", "group", "quantity", "unit", "step", "x", "y"];

    private static readonly string[] FlowKeys =
        ["id", "from", "to", "product", "description", "volume", "unit", "step"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static SupplyChainModel Parse(LineDocument document) => Outcome(document).Model;

    /// <summary>
    /// What the store keeps of <paramref name="document"/>: the model, its problems, and, for a text
    /// that is not YAML, why, so that nothing is edited or written until it is. <b>Never throws.</b>
    /// </summary>
    public static ParseOutcome<SupplyChainModel> Outcome(LineDocument document)
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
            var problem = new ParseProblem(line, $"The document could not be read as YAML: {exception.Message}");
            return new ParseOutcome<SupplyChainModel>(SupplyChainModel.Empty with { Problems = [problem] }, [problem], problem.Message);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new ParseOutcome<SupplyChainModel>(SupplyChainModel.Empty, []);
        }

        List<ParseProblem> problems = [];
        var model = new SupplyChainModel(
            ReadGroups(root, document, problems),
            ReadNodes(root, document, problems),
            ReadFlows(root, document, problems),
            problems,
            ReadVersion(root, document, problems));
        return new ParseOutcome<SupplyChainModel>(model, model.Problems);
    }

    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        if (YamlAccess.Node(root, HeaderKey) is not YamlScalarNode scalar)
        {
            problems.Add(new ParseProblem(0, $"The document does not begin with `{HeaderKey}: {SupplyChainModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != SupplyChainModel.CurrentVersion)
        {
            problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"This document states version {version}; this module reads version {SupplyChainModel.CurrentVersion}."));
        }

        return version;
    }

    private static List<SupplyChainGroup> ReadGroups(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        List<SupplyChainGroup> groups = [];
        foreach (var mapping in Entries(root, GroupsKey, "group", document, problems))
        {
            ReportUnknownKeys(mapping, GroupKeys, document, problems, "group");
            groups.Add(new SupplyChainGroup(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "name") ?? "",
                YamlAccess.Scalar(mapping, "description") ?? "",
                Range(mapping, document),
                Number(mapping, "x", document, problems),
                Number(mapping, "y", document, problems)));
        }

        return groups;
    }

    private static List<SupplyChainNode> ReadNodes(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        List<SupplyChainNode> nodes = [];
        foreach (var mapping in Entries(root, NodesKey, "node", document, problems))
        {
            ReportUnknownKeys(mapping, NodeKeys, document, problems, "node");

            // A legacy stage word reads as the stage it became; the line keeps its word until edited.
            var type = SupplyChainNodeTypes.Normalize(YamlAccess.Scalar(mapping, "type") ?? "");
            if (!SupplyChainNodeTypes.IsKnown(type))
            {
                problems.Add(new ParseProblem(YamlAccess.LineOf(mapping, document), $"`{type}` is not a stage this module knows."));
            }

            nodes.Add(new SupplyChainNode(
                YamlAccess.Scalar(mapping, "id") ?? "",
                type,
                YamlAccess.Scalar(mapping, "name") ?? "",
                YamlAccess.Scalar(mapping, "description") ?? "",
                YamlAccess.Scalar(mapping, "group") ?? "",
                Number(mapping, "quantity", document, problems),
                YamlAccess.Scalar(mapping, "unit") ?? "",
                Number(mapping, "step", document, problems),
                Number(mapping, "x", document, problems),
                Number(mapping, "y", document, problems),
                Range(mapping, document)));
        }

        return nodes;
    }

    private static List<SupplyChainFlow> ReadFlows(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        List<SupplyChainFlow> flows = [];
        foreach (var mapping in Entries(root, FlowsKey, "flow", document, problems))
        {
            ReportUnknownKeys(mapping, FlowKeys, document, problems, "flow");
            flows.Add(new SupplyChainFlow(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "from") ?? "",
                YamlAccess.Scalar(mapping, "to") ?? "",
                YamlAccess.Scalar(mapping, "product") ?? "",
                YamlAccess.Scalar(mapping, "description") ?? "",
                Number(mapping, "volume", document, problems),
                YamlAccess.Scalar(mapping, "unit") ?? "",
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
        List<ParseProblem> problems)
    {
        if (YamlAccess.Node(root, key) is not YamlSequenceNode sequence)
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
                problems.Add(new ParseProblem(YamlAccess.LineOf(node, document), $"A {what} entry is not a mapping and was passed over."));
            }
        }
    }

    private static void ReportUnknownKeys(
        YamlMappingNode mapping,
        string[] known,
        LineDocument document,
        List<ParseProblem> problems,
        string what)
    {
        foreach ((YamlNode key, var _) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name } scalar && !known.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"`{name}` is not a key this module reads on a {what}; the line is kept."));
            }
        }
    }

    /// <summary>
    /// A number from the entry, or <c>null</c> when the key is absent or will not read - the second
    /// of which is reported, because a silently missing value reads as a lost edit.
    /// </summary>
    private static double? Number(YamlMappingNode mapping, string key, LineDocument document, List<ParseProblem> problems)
    {
        if (YamlAccess.Node(mapping, key) is not YamlScalarNode scalar)
        {
            return null;
        }

        if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a number and was ignored."));
        return null;
    }

    private static LineRange Range(YamlNode node, LineDocument document) => YamlNodeRange.Of(node, document.Lines);
}
