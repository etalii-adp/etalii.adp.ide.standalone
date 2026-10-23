using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The reader's own sentences, handed to the constraint evaluator as <see cref="DislReaderFinding"/>s on
/// their lines: a key the reader does not read, a <c>name</c> on a Comment, an entry that is not a
/// mapping, a number that does not read, the header's sentences, and a body that is not YAML (findings
/// B6, B7, B2 and B8 of functional-decomposition-graph-disl-fbl).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for gap 6</b> of the companion: the definition's <c>typeMap</c> keeps host attributes out
/// of CEL and the constraints, and FBL cannot word a body it could not read, so neither language can word
/// these. They are read from what the binding reports: its <c>unknownKeys</c>, its <c>strayName</c>, the
/// scalars of <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> as written, the rule that took an entry
/// that is not a mapping, the header's <c>version</c>, and the library's finding for a body it could not read.
/// </para>
/// <para>
/// <b>They come in the order the tool has always given them</b>: per entry its unknown keys, its type, a
/// Comment's name and its numbers; the elements, then the connections, then the header.
/// </para>
/// <para>
/// <b>An entry of a type the notation does not have is the definition's to report</b>, by its rules
/// <c>unknownElementType</c> and <c>unknownRelationType</c>, so it is only marked here (<see cref="FdgReaderSentence.IsType"/>);
/// <see cref="FdgFindingOrder"/> puts the definition's sentence where the mark stands.
/// </para>
/// <para>
/// <b>No finding of FBL's own is handed on</b> (findings B7, B9 and B18): a missing or repeated id, a
/// reference that names nothing and a repeated key are the definition's to report, or the tool reports
/// nothing, as it never has.
/// </para>
/// </remarks>
internal static class FdgReaderSentences
{
    /// <summary>The header's key, which is also the root's <c>version</c>.</summary>
    private const string HeaderKey = "functional-decomposition-graph";

    /// <summary>The version this tool reads, and the only one.</summary>
    private const int CurrentVersion = 1;

    /// <summary>How the tool begins the sentence of a body it could not read; what follows is the reader's reason.</summary>
    public const string NotYaml = "The document could not be read as YAML: ";

    /// <summary>The library's lead-in to a body that is not well-formed YAML, which the definition words itself.</summary>
    private const string NotYamlLeadIn = "The body is not well-formed YAML: ";

    private static readonly string[] _numbers = ["x", "y", "width"];

    /// <summary>
    /// The planner's refusal <paramref name="reason"/> in the tool's words, where they differ (finding B16): a list the
    /// body does not have is today's missing section. Every other reason is the planner's own.
    /// </summary>
    public static string RefusalOf(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        foreach (var section in (string[])["elements", "connections"])
        {
            if (reason == $"The file has no {section} to add to.") return $"The document has no `{section}:` section to add to.";
        }

        return reason;
    }

    /// <summary>The reader's findings of <paramref name="body"/>, in reading order, for the evaluator.</summary>
    public static IReadOnlyList<DislReaderFinding> Of(FdgBody body) =>
        [.. All(body).Where(sentence => !sentence.IsType).Select(sentence => sentence.NotParsed
            ? DislReaderFinding.NotParsed(sentence.Message, sentence.Line + 1)
            : DislReaderFinding.Unreadable(sentence.Message, sentence.Line + 1))];

    /// <summary>Every sentence of <paramref name="body"/> in reading order, with a mark where an entry's type is one the notation does not have.</summary>
    public static IReadOnlyList<FdgReaderSentence> All(FdgBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var reading = body.Model;
        if (FdgReading.IsUnreadable(reading))
        {
            var finding = reading.Findings.FirstOrDefault(candidate => candidate.Code is FindingCodes.Unparseable or FindingCodes.DuplicateKey) ?? reading.Findings.FirstOrDefault();
            return [new FdgReaderSentence(LineOf(finding), YamlMessage(finding), NotParsed: true, IsType: false)];
        }

        if (reading.Elements.FirstOrDefault(element => element.Type == FdgReading.Graph) is not { } graph)
        {
            // An empty body, or one whose root is not a mapping, declares nothing, and nothing is said about it.
            return [];
        }

        List<FdgReaderSentence> sentences = [];
        foreach (var element in reading.Elements.Where(element => element.Rule.StartsWith("element", StringComparison.Ordinal) || FdgReading.IsElement(element)))
        {
            Entry(body, element, "element", sentences);
        }

        foreach (var connection in reading.Elements.Where(element => element.Rule.StartsWith("connection", StringComparison.Ordinal) || FdgReading.IsConnection(element)))
        {
            Entry(body, connection, "connection", sentences);
        }

        Header(body, graph, sentences);
        return sentences;
    }

    private static void Entry(FdgBody body, FblElement entry, string what, List<FdgReaderSentence> sentences)
    {
        var range = body.LinesOf(entry.OwnSpan);
        if (entry.Type == FdgReading.Unreadable)
        {
            sentences.Add(new FdgReaderSentence(range.Start, $"A{(what == "element" ? "n" : "")} {what} entry is not a mapping and was passed over."));
            return;
        }

        if (entry.Attributes.GetValueOrDefault("unknownKeys") is IEnumerable<object?> keys)
        {
            foreach (var key in keys.OfType<string>())
            {
                sentences.Add(new FdgReaderSentence(body.KeyLine(range, key), $"`{key}` is not a key this module reads on a {what}; the line is kept."));
            }
        }

        if (entry.Type is FdgReading.UnknownElement or FdgReading.UnknownConnection)
        {
            sentences.Add(new FdgReaderSentence(range.Start, "", IsType: true));
        }

        if (what == "connection")
        {
            return;
        }

        var isComment = entry.Type == "Comment";
        if (isComment && FdgReading.TextOf(entry, "strayName") is not null)
        {
            sentences.Add(new FdgReaderSentence(range.Start, "A Comment has no name; the `name` here is kept and ignored."));
        }

        foreach (var key in isComment ? [.. _numbers, "height"] : _numbers)
        {
            if (FdgReading.TextOf(entry, key) is { } text && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                sentences.Add(new FdgReaderSentence(body.KeyLine(range, key), $"`{key}: {text}` is not a number; zero was used."));
            }
        }
    }

    /// <summary>The header's sentences: a missing one, a version that is not a number, and a version that is not this tool's.</summary>
    private static void Header(FdgBody body, FblElement graph, List<FdgReaderSentence> sentences)
    {
        if (FdgReading.TextOf(graph, "version") is not { } text)
        {
            sentences.Add(new FdgReaderSentence(0, $"The document does not begin with `{HeaderKey}: {CurrentVersion}`."));
            return;
        }

        var line = body.KeyLine(new LineRange(0, body.Lines.Count - 1), HeaderKey);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            sentences.Add(new FdgReaderSentence(line, $"The version `{text}` is not a number."));
        }
        else if (version != CurrentVersion)
        {
            sentences.Add(new FdgReaderSentence(line, $"This document states version {version}; this module reads version {CurrentVersion}."));
        }
    }

    /// <summary>The library's sentence for a body it could not read, without its lead-in, which the definition words itself.</summary>
    private static string YamlMessage(Finding? finding)
    {
        var message = finding?.Message ?? "it is not well-formed";
        return message.StartsWith(NotYamlLeadIn, StringComparison.Ordinal) ? message[NotYamlLeadIn.Length..] : message;
    }

    private static int LineOf(Finding? finding) => Math.Max(0, (finding?.Location?.Line ?? 1) - 1);
}

/// <summary>One of the reader's sentences, on its 0-based line, or the mark of an entry whose type the notation does not have.</summary>
/// <param name="Line">The 0-based line it is reported on.</param>
/// <param name="Message">The sentence; for a body that could not be read, the reader's reason alone.</param>
/// <param name="NotParsed">Whether the body could not be read at all.</param>
/// <param name="IsType">Whether this marks where the definition's sentence for an unknown type stands, and says nothing itself.</param>
internal sealed record FdgReaderSentence(int Line, string Message, bool NotParsed = false, bool IsType = false);
