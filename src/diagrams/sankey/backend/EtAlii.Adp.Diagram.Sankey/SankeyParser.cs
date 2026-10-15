using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="SankeyModel"/>, recording which lines
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
/// key or a number that will not read is kept, passed over and reported. A half-typed document must
/// open and keep every byte its author wrote.
/// </para>
/// </remarks>
public static class SankeyParser
{
    internal const string HeaderKey = "sankey";
    private const string FormatKey = "format";
    private const string FlowColorKey = "flow-color";
    internal const string ThicknessKey = "thickness";
    internal const string NodesKey = "nodes";
    internal const string FlowsKey = "flows";

    /// <summary>The text of a new, empty <c>.skv</c> document.</summary>
    /// <remarks>
    /// <para>
    /// The header and the two sections written flow-empty - <c>nodes: []</c> and <c>flows: []</c> -
    /// because a bare <c>nodes:</c> parses as a null rather than an empty sequence. The first added
    /// entry opens the flow form into a block one, which <c>LineSplice.InsertionPointFor</c> does.
    /// </para>
    /// <para>CRLF, the repository's house style, because a new file has no style of its own to keep.</para>
    /// </remarks>
    internal static string EmptyDocument { get; } = string.Join(
        "\r\n",
        $"{HeaderKey}: {SankeyModel.CurrentVersion}",
        $"{NodesKey}: []",
        $"{FlowsKey}: []") + "\r\n";

    private static readonly string[] RootKeys = [HeaderKey, FormatKey, FlowColorKey, ThicknessKey, NodesKey, FlowsKey];

    private static readonly string[] NodeKeys = ["id", "name", "color", "note", "format", "column", "description"];

    private static readonly string[] FlowKeys = ["id", "from", "to", "value", "step", "color", "description"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static SankeyModel Parse(LineDocument document) => Outcome(document).Model;

    /// <summary>
    /// What the store keeps of <paramref name="document"/>: the model, its problems, and, for a text
    /// that is not YAML, why, so that nothing is edited or written until it is. <b>Never throws.</b>
    /// </summary>
    public static ParseOutcome<SankeyModel> Outcome(LineDocument document)
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
            return new ParseOutcome<SankeyModel>(SankeyModel.Empty with { Problems = [problem] }, [problem], problem.Message);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new ParseOutcome<SankeyModel>(SankeyModel.Empty, []);
        }

        List<ParseProblem> problems = [];
        ReportUnknownKeys(root, RootKeys, document, problems, "diagram");
        var model = new SankeyModel(
            ReadSettings(root, document, problems),
            ReadNodes(root, document, problems),
            ReadFlows(root, document, problems),
            problems,
            ReadVersion(root, document, problems));
        return new ParseOutcome<SankeyModel>(model, model.Problems);
    }

    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        if (YamlAccess.Node(root, HeaderKey) is not YamlScalarNode scalar)
        {
            problems.Add(new ParseProblem(0, $"The document does not begin with `{HeaderKey}: {SankeyModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != SankeyModel.CurrentVersion)
        {
            problems.Add(new ParseProblem(YamlAccess.LineOf(scalar, document), $"This document states version {version}; this module reads version {SankeyModel.CurrentVersion}."));
        }

        return version;
    }

    private static SankeySettings ReadSettings(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        var settings = SankeySettings.Default;

        if (YamlAccess.Node(root, FormatKey) is YamlScalarNode format)
        {
            settings = settings with { Format = format.Value ?? "", FormatLine = YamlAccess.LineOf(format, document) };
        }

        if (YamlAccess.Node(root, FlowColorKey) is YamlScalarNode flowColor)
        {
            var value = flowColor.Value ?? "";
            if (value is SankeySettings.FromSource or SankeySettings.FromTarget)
            {
                settings = settings with { FlowColor = value };
            }
            else
            {
                problems.Add(new ParseProblem(YamlAccess.LineOf(flowColor, document), $"`{FlowColorKey}: {value}` is neither `{SankeySettings.FromSource}` nor `{SankeySettings.FromTarget}`; flows take their target's colour."));
            }

            settings = settings with { FlowColorLine = YamlAccess.LineOf(flowColor, document) };
        }

        if (YamlAccess.Node(root, ThicknessKey) is YamlScalarNode thickness)
        {
            if (double.TryParse(thickness.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0)
            {
                settings = settings with { Thickness = Math.Clamp(value, SankeyGeometry.MinimumScale, SankeyGeometry.MaximumScale) };
            }
            else
            {
                problems.Add(new ParseProblem(YamlAccess.LineOf(thickness, document), $"`{ThicknessKey}: {thickness.Value}` is not a number above zero and was ignored."));
            }

            settings = settings with { ThicknessLine = YamlAccess.LineOf(thickness, document) };
        }

        return settings;
    }

    private static List<SankeyNode> ReadNodes(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        List<SankeyNode> nodes = [];
        foreach (var mapping in Entries(root, NodesKey, "node", document, problems))
        {
            ReportUnknownKeys(mapping, NodeKeys, document, problems, "node");

            int? column = null;
            if (Number(mapping, "column", document, problems) is { } stated)
            {
                if (stated >= 1 && Math.Abs(stated - Math.Floor(stated)) < double.Tolerance)
                {
                    column = (int)Math.Min(stated, 1000);
                }
                else
                {
                    problems.Add(new ParseProblem(YamlAccess.LineOf(mapping, document), $"`column: {stated.ToString(CultureInfo.InvariantCulture)}` is not a whole number from 1; the flows place this node."));
                }
            }

            nodes.Add(new SankeyNode(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "name") ?? "",
                YamlAccess.Scalar(mapping, "color") ?? "",
                YamlAccess.Scalar(mapping, "note") ?? "",
                YamlAccess.Scalar(mapping, "format") ?? "",
                column,
                YamlAccess.Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return nodes;
    }

    private static List<SankeyFlow> ReadFlows(YamlMappingNode root, LineDocument document, List<ParseProblem> problems)
    {
        List<SankeyFlow> flows = [];
        foreach (var mapping in Entries(root, FlowsKey, "flow", document, problems))
        {
            ReportUnknownKeys(mapping, FlowKeys, document, problems, "flow");
            flows.Add(new SankeyFlow(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "from") ?? "",
                YamlAccess.Scalar(mapping, "to") ?? "",
                Number(mapping, "value", document, problems),
                Number(mapping, "step", document, problems),
                YamlAccess.Scalar(mapping, "color") ?? "",
                YamlAccess.Scalar(mapping, "description") ?? "",
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
