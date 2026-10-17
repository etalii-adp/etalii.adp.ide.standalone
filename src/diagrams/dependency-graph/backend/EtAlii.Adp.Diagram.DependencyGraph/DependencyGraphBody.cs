using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One <c>.dgr</c> body, read through FBL: the module's binding (<c>dependency-graph.fbl</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the one DISL model built from that
/// reading, which the menus, the rows, the findings and the payloads are derived from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reading is put in the tool's order before the model is built</b> (<see cref="DependencyGraphReading"/>),
/// which no binding can state: nodes are read before dependencies whatever the file's order.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately (<see cref="OpenBody.Fork"/>)
/// rather than reading it once more. Each fork is its own body, so an edit to one is never seen by another.
/// </para>
/// <para>
/// Writing still goes through <see cref="DependencyGraphWriter"/> over the parser's line ranges
/// (dependency-graph-disl-fbl, task 7); task 8 writes through the binding.
/// </para>
/// </remarks>
public sealed class DependencyGraphBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(DependencyGraphBody).Assembly, "dependency-graph.fbl", "dgr"));

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<DependencyGraphReading> _reading = new();
    private readonly TextMemo<DislModel> _disl = new();

    private DependencyGraphBody(OpenBody body)
    {
        _body = body;
        _edits = new FblBodyEdits(body, Recent);
    }

    /// <summary>The binding every <c>.dgr</c> body is read with.</summary>
    internal static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>What the binding reads from the body now.</summary>
    public FblModel Model => _model.Of(_body.Bytes, () => _body.Model);

    /// <summary>The binding's reading in the order the tool reads it: nodes first.</summary>
    internal DependencyGraphReading Reading => _reading.Of(_body.Bytes, () => DependencyGraphReading.Of(Model));

    /// <summary>The DISL model of what the binding reads now, under the bundled definition.</summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(Reading.Model, DependencyGraphDefinition.Specification));

    /// <summary>A DISL model of its own, for an operation to run on and change.</summary>
    internal DislDiagram Working() => DislModelBuilder.From(Reading.Model, DependencyGraphDefinition.Specification).Diagram;

    /// <summary>Whether the body could not be read at all.</summary>
    public bool IsUnreadable => Model.Unreadable;

    /// <summary>A body for <paramref name="text"/>. Never throws on content: an unreadable body reads as such.</summary>
    public static DependencyGraphBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new DependencyGraphBody(known.Fork());

        var body = OpenBody.Open(bytes, Binding, new FblOptions { FileName = "body.dgr" });
        Recent.Remember(body.Fork());
        return new DependencyGraphBody(body);
    }

    /// <summary>The body <paramref name="entry"/> was read with, which the store reads beside the parser's model.</summary>
    internal static DependencyGraphBody Of(DocumentEntry<LineDocument, DependencyGraphModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Model.Body ?? Parse(entry.Document.Text);
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    private LineRange LinesOf(Span span) => _edits.LinesOf(span);

    /// <summary>
    /// The model the writer splices at, for <paramref name="document"/>: the parser's, with its line ranges;
    /// or, for a body the binding reads and the parser does not (a repeated key, Q2), one with the ranges
    /// of the binding's entries, so an edit lands where it would in any other body.
    /// </summary>
    internal static DependencyGraphModel Ranges(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var parsed = DependencyGraphParser.Outcome(document);
        if (parsed.IsUsable) return parsed.Model;

        var body = Parse(document.Text);
        if (body.IsUnreadable) return DependencyGraphModel.Empty;

        var elements = body.Model.Elements;
        return new DependencyGraphModel(
            [
                .. elements.Where(element => element.Type == "Node").Select(element => new DependencyGraphElement(
                    DependencyGraphReading.TextOf(element, "storedId"),
                    DependencyGraphReading.TextOf(element, "label"),
                    double.TryParse(DependencyGraphReading.TextOf(element, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0d,
                    int.TryParse(DependencyGraphReading.TextOf(element, "row"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) ? row : 0,
                    body.LinesOf(element.OwnSpan))),
            ],
            [
                .. elements.Where(element => element.Type == "Dependency").Select(element => new DependencyGraphRelation(
                    DependencyGraphReading.TextOf(element, "storedId"),
                    DependencyGraphReading.TextOf(element, "writtenFrom"),
                    DependencyGraphReading.TextOf(element, "writtenTo"),
                    DependencyGraphReading.TextOf(element, "label"),
                    body.LinesOf(element.OwnSpan))),
            ]);
    }
}
