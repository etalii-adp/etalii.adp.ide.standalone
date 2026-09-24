using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Reads a <see cref="LineDocument"/> into an <see cref="FdgModel"/>, recording which lines
/// declare what.
/// </summary>
/// <remarks>
/// <para>
/// <b>YamlDotNet reads and never writes.</b> It handles quoting, block scalars, anchors and
/// aliases correctly and gives a line number for every node, which is what lets an entry know its
/// own lines. Nothing is serialised back through it: every write is a splice through
/// <see cref="LineSplice"/>, which is what makes an unchanged document byte-identical
/// (Requirement 2.3) rather than merely equivalent.
/// </para>
/// <para>
/// <b>THIS PARSER NEVER THROWS, AND THIS IS WHERE THE MIRROR DELIBERATELY STOPS.</b> The design
/// says each component mirrors its <c>dependency-graph</c> counterpart "unless noted", and this is
/// the noted one - so a later reader finding the difference is meeting a decision rather than a
/// mirror that drifted, and should not "restore" it. <b>The counterpart's own summary reads
/// "Throws only when the text is not YAML"</b>; here a YAML error produces an empty model, the
/// document text is kept, and the problem is reported with its line, which the design states in as
/// many words. The reason is that a <c>.fdg</c> document is edited by hand and by the canvas
/// alternately: a half-typed document must open, keep every byte the author wrote, and say what is
/// wrong, rather than failing to open and losing the editing session.
/// </para>
/// <para>
/// <b>What it does not understand, it passes over.</b> An unknown key, an unknown type, a
/// malformed entry: the entry is kept with whatever could be read, the lines survive because
/// nothing rewrites them, and a problem is recorded for the validator. A coordinate that will not
/// parse is zero, and is reported - it is not a reason to drop the element.
/// </para>
/// </remarks>
public static class FdgParser
{
    private const string HeaderKey = "functional-decomposition-graph";
    private const string ElementsKey = "elements";
    private const string ConnectionsKey = "connections";

    /// <summary>The keys an element entry may carry. Anything else survives and is reported.</summary>
    private static readonly string[] ElementKeys =
        ["id", "type", "name", "description", "text", "x", "y", "width", "height"];

    /// <summary>The keys a connection entry may carry.</summary>
    private static readonly string[] ConnectionKeys =
        ["id", "type", "from", "to", "name", "description"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static FdgModel Parse(LineDocument document)
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
            // The one case the design names: not YAML at all. The text is the caller's and is
            // untouched; what comes back is an empty model carrying the reason and the line, so
            // the document still opens and the panel still says what is wrong.
            var line = Math.Max(0, (int)exception.Start.Line - 1);
            return FdgModel.Empty with { Problems = [new FdgProblem(line, $"The document could not be read as YAML: {exception.Message}")] };
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            // An empty file, or a root that is not a mapping, declares nothing. That is a model
            // with nothing in it rather than a failure: whether an empty document is worth
            // complaining about is the rule set's decision, not the parser's.
            return FdgModel.Empty;
        }

        List<FdgProblem> problems = [];
        return new FdgModel(
            ReadElements(root, document, problems),
            ReadConnections(root, document, problems),
            problems,
            ReadVersion(root, document, problems));
    }

    /// <summary>
    /// The header's version. A missing or unreadable header opens the document and is reported,
    /// as <c>.dgr</c> does - it is never a reason to refuse the file.
    /// </summary>
    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<FdgProblem> problems)
    {
        var node = Node(root, HeaderKey);
        if (node is not YamlScalarNode scalar)
        {
            problems.Add(new FdgProblem(0, $"The document does not begin with `{HeaderKey}: {FdgModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new FdgProblem(LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != FdgModel.CurrentVersion)
        {
            problems.Add(new FdgProblem(LineOf(scalar, document), $"This document states version {version}; this module reads version {FdgModel.CurrentVersion}."));
        }

        return version;
    }

    private static List<FdgElement> ReadElements(YamlMappingNode root, LineDocument document, List<FdgProblem> problems)
    {
        List<FdgElement> elements = [];
        foreach (var node in Sequence(root, ElementsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new FdgProblem(LineOf(node, document), "An element entry is not a mapping and was passed over."));
                continue;
            }

            var type = Scalar(mapping, "type") ?? "";
            var isComment = string.Equals(type, FdgElementTypes.Comment, StringComparison.Ordinal);
            ReportUnknownKeys(mapping, ElementKeys, document, problems, "element");

            if (!FdgElementTypes.IsKnown(type))
            {
                problems.Add(new FdgProblem(LineOf(mapping, document), $"`{type}` is not an element type this module knows."));
            }

            if (isComment && Scalar(mapping, "name") is not null)
            {
                // Named rather than dropped: the line survives the round trip, and the author is
                // told the name means nothing on a Comment.
                problems.Add(new FdgProblem(LineOf(mapping, document), "A Comment has no name; the `name` here is kept and ignored."));
            }

            elements.Add(new FdgElement(
                Scalar(mapping, "id") ?? "",
                type,
                Scalar(mapping, "name") ?? "",
                Scalar(mapping, "description") ?? "",
                Scalar(mapping, "text") ?? "",
                Number(mapping, "x", document, problems),
                Number(mapping, "y", document, problems),
                Number(mapping, "width", document, problems),
                isComment ? Number(mapping, "height", document, problems) : null,
                Range(mapping, document)));
        }

        return elements;
    }

    private static List<FdgConnection> ReadConnections(YamlMappingNode root, LineDocument document, List<FdgProblem> problems)
    {
        List<FdgConnection> connections = [];
        foreach (var node in Sequence(root, ConnectionsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new FdgProblem(LineOf(node, document), "A connection entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(mapping, ConnectionKeys, document, problems, "connection");

            var type = Scalar(mapping, "type") ?? "";
            if (!FdgConnectionTypes.IsKnown(type))
            {
                problems.Add(new FdgProblem(LineOf(mapping, document), $"`{type}` is not a relation this module knows."));
            }

            connections.Add(new FdgConnection(
                Scalar(mapping, "id") ?? "",
                type,
                Scalar(mapping, "from") ?? "",
                Scalar(mapping, "to") ?? "",
                Scalar(mapping, "name") ?? "",
                Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return connections;
    }

    private static void ReportUnknownKeys(
        YamlMappingNode mapping,
        string[] known,
        LineDocument document,
        List<FdgProblem> problems,
        string what)
    {
        foreach (var (key, _) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name } scalar && !known.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new FdgProblem(LineOf(scalar, document), $"`{name}` is not a key this module reads on a {what}; the line is kept."));
            }
        }
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode root, string key) =>
        Node(root, key) is YamlSequenceNode sequence ? sequence.Children : [];

    private static YamlNode? Node(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        Node(mapping, key) is YamlScalarNode scalar ? scalar.Value ?? "" : null;

    /// <summary>
    /// A number from the entry, or zero. <b>Invariant culture</b>, so a document written where the
    /// decimal separator is a comma says the same thing everywhere.
    /// </summary>
    /// <remarks>
    /// A value that will not read is zero AND a problem: silently defaulting would put the element
    /// at the origin with nothing said, which is the shape of bug that gets reported as "the
    /// diagram moved my box".
    /// </remarks>
    private static double Number(YamlMappingNode mapping, string key, LineDocument document, List<FdgProblem> problems)
    {
        if (Node(mapping, key) is not YamlScalarNode scalar)
        {
            return 0;
        }

        if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add(new FdgProblem(LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a number; zero was used."));
        return 0;
    }

    private static int LineOf(YamlNode node, LineDocument document) =>
        Math.Clamp((int)node.Start.Line - 1, 0, Math.Max(0, document.Lines.Count - 1));

    /// <summary>
    /// The lines an entry occupies - what an edit rewrites, and nothing else.
    /// </summary>
    /// <remarks>
    /// YamlDotNet's end mark sits at the start of whatever follows, so a range taken raw swallows
    /// the next entry's first line; column 1 is the tell, and the end is pulled back one. Trailing
    /// blank lines and comments are then walked back off the end, because they belong to the
    /// author rather than to the entry - splicing over them is how a comment between two entries
    /// disappears on the first edit.
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
    /// <b>A mapping's own <c>End</c> does not cover its children</b>, so a range taken from it
    /// alone stops short - and a short range is not a harmless approximation: <c>SetKey</c> then
    /// fails to find the key inside it and INSERTS a second one, which is how an edit that should
    /// have rewritten two lines added two instead and left the document with two <c>x:</c> keys.
    /// Measured here before it was fixed, and it is why the counterpart carries this same walk.
    /// An alias resolves to a node YAML requires to have been declared earlier, so following one
    /// can only look backwards and never stretches a range past where the entry really ends.
    /// </remarks>
    private static Mark EndMark(YamlNode node)
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
