using System.Diagnostics.CodeAnalysis;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One Turtle or N-Triples body, read through the module's binding (<c>w3c-turtle.fbl#turtle</c>, embedded) by its
/// persistence plugin <see cref="RdfTurtlePlugin"/>, and shared by the four readings: the store holds one per open
/// file, so the RDF graph, the OWL ontology, the SHACL shapes and the SKOS concept scheme of one file read one body,
/// one model and one history, and a save's reparse gives every session the new body at once (Requirement 7.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The commands still write its lines</b> (Q5, stage one): <see cref="Lines"/> is what <see cref="RdfWriter"/> and
/// the SHACL writer splice, and <see cref="RdfEditHandler{TCommand}"/> tells the body when they have, so that its
/// <see cref="Reading"/> is read again. The projections still read the parser's <see cref="RdfModel"/>, which the store
/// keeps beside it; what the plugin reads is checked against that model for the whole corpus, not yet drawn.
/// </para>
/// <para>
/// <b>The plugin is told the body's syntax by the body's name</b>, the stand-in for gap 5 (<see cref="RdfSyntax"/>).
/// The store hands a body its text alone, as an edit's copy is (<see cref="ITextBody{TSelf}.Parse"/>), and then tells it
/// the path it is at (<see cref="At"/>).
/// </para>
/// <para>
/// <b>Its bytes are its text's, after the byte-order mark it was opened with</b>, so a span the plugin reports counts
/// that mark. The store reads a file's text without its mark, and its save writes the mark back (#806), so a body the
/// store holds has none.
/// </para>
/// </remarks>
public sealed class RdfBody : ITextBody<RdfBody>
{
    /// <summary>The name a body is given before the store tells it its path.</summary>
    private const string Unplaced = "body.ttl";

    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];

    private readonly bool _marked;

    private RdfBody(LineDocument lines, string fileName, bool marked)
    {
        Lines = lines;
        FileName = fileName;
        _marked = marked;
    }

    /// <summary>The body's lines: what the commands' writers splice.</summary>
    public LineDocument Lines { get; }

    /// <summary>The name of the file the body is at; <c>body.ttl</c> until it is told.</summary>
    private string FileName { get; }

    /// <summary>The syntax the body's name says it is written in.</summary>
    public RdfSyntax Syntax => SyntaxOf(FileName);

    /// <inheritdoc />
    public string Text => Lines.Text;

    /// <summary>The body's bytes, as the plugin reads them: its byte-order mark, if it was opened with one, and its text.</summary>
    public byte[] Bytes => Plugin.Bytes;

    /// <summary>What the plugin reads from the body now: its <c>Triple</c>, <c>Prefix</c> and <c>Base</c> elements and its findings.</summary>
    public FblModel Reading => Plugin.Model;

    /// <summary>Whether the body is read-only: when it does not parse.</summary>
    public bool IsReadOnly => Plugin.IsReadOnly;

    [AllowNull]
    private PluginBody Plugin
    {
        get => field ??= Read();
        set;
    }

    /// <summary>A body for <paramref name="text"/>, not yet told its path. Never throws on content.</summary>
    public static RdfBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new RdfBody(LineDocument.Parse(text), Unplaced, false);
    }

    /// <summary>A body for the bytes of the file at <paramref name="path"/>, a byte-order mark among them. Never throws on content.</summary>
    public static RdfBody Open(byte[] bytes, string path)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var marked = bytes.AsSpan().StartsWith(_byteOrderMark);
        var mark = marked ? _byteOrderMark.Length : 0;
        return new RdfBody(LineDocument.Parse(Encoding.UTF8.GetString(bytes, mark, bytes.Length - mark)), Path.GetFileName(path), marked);
    }

    /// <summary>
    /// This body, at <paramref name="path"/>: itself when that is the file it was told, else a body over the same lines
    /// at that file, which takes its place.
    /// </summary>
    public RdfBody At(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var name = Path.GetFileName(path);
        return string.Equals(name, FileName, StringComparison.Ordinal) ? this : new RdfBody(Lines, name, _marked);
    }

    /// <summary>The syntax a file of <paramref name="fileName"/> is written in: N-Triples for <c>.nt</c>, Turtle otherwise.</summary>
    internal static RdfSyntax SyntaxOf(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".nt", StringComparison.OrdinalIgnoreCase) ? RdfSyntax.NTriples : RdfSyntax.Turtle;

    /// <summary>
    /// What the binding's host side answers a change of the body with: in this stage a refusal, in today's sentence for
    /// a body that does not parse, and in the plugin's for any other, since no command writes through a plan yet (Q5).
    /// </summary>
    internal PlanResult Plan(ModelChange change) => Plugin.Plan(change);

    /// <summary>Told by a command's handler that a writer changed <see cref="Lines"/>, so the plugin reads the body again when asked.</summary>
    internal void Changed() => Plugin = null;

    /// <summary>The body read through the binding, the plugin told the syntax, and refusing a plan in today's sentence when it does not parse (F1, B14).</summary>
    private PluginBody Read()
    {
        var text = Lines.Text;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (_marked)
        {
            bytes = [.. _byteOrderMark, .. bytes];
        }

        var outcome = RdfParser.Outcome(Lines);
        var unreadable = outcome.IsUsable ? null : $"{FileName} could not be read, so nothing can be edited until it can: {outcome.Unreadable}";
        return PluginBody.Open(bytes, RdfDefinitions.Binding, new RdfTurtlePlugin(Syntax), FileName, unreadable);
    }
}
