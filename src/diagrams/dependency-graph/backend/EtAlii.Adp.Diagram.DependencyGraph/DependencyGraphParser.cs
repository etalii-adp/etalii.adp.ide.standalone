using System.Globalization;
using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;
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
    private static readonly ILogger _logger = Log.ForContext(typeof(DependencyGraphParser));

    private const string ElementsKey = "elements";
    private const string RelationsKey = "relations";

    /// <summary>
    /// Reads the document, <b>without throwing</b>: a body that is not YAML is an outcome that could
    /// not be read, with the YAML reader's sentence as its reason and its one problem, on the line the
    /// reader names, and an empty model.
    /// </summary>
    /// <remarks>A file that is not YAML is an ordinary state for a file somebody is editing (code-structure FR-049).</remarks>
    public static ParseOutcome<DependencyGraphModel> Outcome(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            return new ParseOutcome<DependencyGraphModel>(Parse(document), []);
        }
        catch (YamlException exception)
        {
            _logger.Debug(exception, "A dependency graph does not parse at line {Line}", exception.Start.Line);
            var line = (int)Math.Max(exception.Start.Line, 1) - 1;
            return new ParseOutcome<DependencyGraphModel>(DependencyGraphModel.Empty, [new ParseProblem(line, exception.Message)], exception.Message);
        }
    }

    /// <summary>Reads the document. Throws only when the text is not YAML.</summary>
    /// <exception cref="YamlException">The document is not well-formed YAML.</exception>
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
        foreach (var node in YamlAccess.Sequence(root, ElementsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            elements.Add(new DependencyGraphElement(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "label") ?? "",
                X(YamlAccess.Scalar(mapping, "x")),
                Row(YamlAccess.Scalar(mapping, "row")),
                YamlNodeRange.Of(mapping, document.Lines)));
        }

        return elements;
    }

    private static List<DependencyGraphRelation> ReadRelations(YamlMappingNode root, LineDocument document)
    {
        var relations = new List<DependencyGraphRelation>();
        foreach (var node in YamlAccess.Sequence(root, RelationsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                continue;
            }

            relations.Add(new DependencyGraphRelation(
                YamlAccess.Scalar(mapping, "id") ?? "",
                YamlAccess.Scalar(mapping, "from") ?? "",
                YamlAccess.Scalar(mapping, "to") ?? "",
                YamlAccess.Scalar(mapping, "label") ?? "",
                YamlNodeRange.Of(mapping, document.Lines)));
        }

        return relations;
    }

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
