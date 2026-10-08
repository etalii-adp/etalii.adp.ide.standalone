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

    private static readonly string[] RootKeys = [HeaderKey, FormatKey, FlowColorKey, ThicknessKey, NodesKey, FlowsKey];

    private static readonly string[] NodeKeys = ["id", "name", "color", "note", "format", "column", "description"];

    private static readonly string[] FlowKeys = ["id", "from", "to", "value", "step", "color", "description"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static SankeyModel Parse(LineDocument document)
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
            return SankeyModel.Empty with { Problems = [new SankeyProblem(line, $"The document could not be read as YAML: {exception.Message}")] };
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return SankeyModel.Empty;
        }

        List<SankeyProblem> problems = [];
        ReportUnknownKeys(root, RootKeys, document, problems, "diagram");
        return new SankeyModel(
            ReadSettings(root, document, problems),
            ReadNodes(root, document, problems),
            ReadFlows(root, document, problems),
            problems,
            ReadVersion(root, document, problems));
    }

    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<SankeyProblem> problems)
    {
        if (Node(root, HeaderKey) is not YamlScalarNode scalar)
        {
            problems.Add(new SankeyProblem(0, $"The document does not begin with `{HeaderKey}: {SankeyModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new SankeyProblem(LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != SankeyModel.CurrentVersion)
        {
            problems.Add(new SankeyProblem(LineOf(scalar, document), $"This document states version {version}; this module reads version {SankeyModel.CurrentVersion}."));
        }

        return version;
    }

    private static SankeySettings ReadSettings(YamlMappingNode root, LineDocument document, List<SankeyProblem> problems)
    {
        var settings = SankeySettings.Default;

        if (Node(root, FormatKey) is YamlScalarNode format)
        {
            settings = settings with { Format = format.Value ?? "", FormatLine = LineOf(format, document) };
        }

        if (Node(root, FlowColorKey) is YamlScalarNode flowColor)
        {
            var value = flowColor.Value ?? "";
            if (value is SankeySettings.FromSource or SankeySettings.FromTarget)
            {
                settings = settings with { FlowColor = value };
            }
            else
            {
                problems.Add(new SankeyProblem(LineOf(flowColor, document), $"`{FlowColorKey}: {value}` is neither `{SankeySettings.FromSource}` nor `{SankeySettings.FromTarget}`; flows take their target's colour."));
            }

            settings = settings with { FlowColorLine = LineOf(flowColor, document) };
        }

        if (Node(root, ThicknessKey) is YamlScalarNode thickness)
        {
            if (double.TryParse(thickness.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0)
            {
                settings = settings with { Thickness = Math.Clamp(value, SankeyGeometry.MinimumScale, SankeyGeometry.MaximumScale) };
            }
            else
            {
                problems.Add(new SankeyProblem(LineOf(thickness, document), $"`{ThicknessKey}: {thickness.Value}` is not a number above zero and was ignored."));
            }

            settings = settings with { ThicknessLine = LineOf(thickness, document) };
        }

        return settings;
    }

    private static List<SankeyNode> ReadNodes(YamlMappingNode root, LineDocument document, List<SankeyProblem> problems)
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
                    problems.Add(new SankeyProblem(LineOf(mapping, document), $"`column: {stated.ToString(CultureInfo.InvariantCulture)}` is not a whole number from 1; the flows place this node."));
                }
            }

            nodes.Add(new SankeyNode(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "name") ?? "",
                Scalar(mapping, "color") ?? "",
                Scalar(mapping, "note") ?? "",
                Scalar(mapping, "format") ?? "",
                column,
                Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return nodes;
    }

    private static List<SankeyFlow> ReadFlows(YamlMappingNode root, LineDocument document, List<SankeyProblem> problems)
    {
        List<SankeyFlow> flows = [];
        foreach (var mapping in Entries(root, FlowsKey, "flow", document, problems))
        {
            ReportUnknownKeys(mapping, FlowKeys, document, problems, "flow");
            flows.Add(new SankeyFlow(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "from") ?? "",
                Scalar(mapping, "to") ?? "",
                Number(mapping, "value", document, problems),
                Number(mapping, "step", document, problems),
                Scalar(mapping, "color") ?? "",
                Scalar(mapping, "description") ?? "",
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
        List<SankeyProblem> problems)
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
                problems.Add(new SankeyProblem(LineOf(node, document), $"A {what} entry is not a mapping and was passed over."));
            }
        }
    }

    private static void ReportUnknownKeys(
        YamlMappingNode mapping,
        string[] known,
        LineDocument document,
        List<SankeyProblem> problems,
        string what)
    {
        foreach ((YamlNode key, var _) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name } scalar && !known.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new SankeyProblem(LineOf(scalar, document), $"`{name}` is not a key this module reads on a {what}; the line is kept."));
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
    private static double? Number(YamlMappingNode mapping, string key, LineDocument document, List<SankeyProblem> problems)
    {
        if (Node(mapping, key) is not YamlScalarNode scalar)
        {
            return null;
        }

        if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        problems.Add(new SankeyProblem(LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a number and was ignored."));
        return null;
    }

    private static int LineOf(YamlNode node, LineDocument document) =>
        Math.Clamp((int)node.Start.Line - 1, 0, Math.Max(0, document.Lines.Count - 1));

    private static LineRange Range(YamlNode node, LineDocument document) => YamlNodeRange.Of(node, document.Lines);
}
