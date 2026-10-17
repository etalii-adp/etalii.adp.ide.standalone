using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One <c>.dgr</c> body, read and written through FBL: the module's binding (<c>dependency-graph.fbl</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the one DISL model built from that
/// reading, which the menus, the rows, the findings and the payloads are derived from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module parses or splices YAML.</b> Reading is the library's reading of the binding's
/// rules; every write is a <see cref="ModelChange"/> the library plans as splices in the body's own
/// conventions (FBL §6), so an unchanged document stays byte-identical and unknown keys, comments and
/// blank lines survive because no splice touches them. Undo stays the host's: the shared restore command
/// puts the text back.
/// </para>
/// <para>
/// <b>The reading is put in the tool's order before the model is built</b> (<see cref="DependencyGraphReading"/>),
/// which no binding can state: nodes are read before dependencies whatever the file's order.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately (<see cref="OpenBody.Fork"/>)
/// rather than reading it once more. Each fork is its own body, so an edit to one is never seen by another.
/// </para>
/// </remarks>
public sealed class DependencyGraphBody : ITextBody<DependencyGraphBody>
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

    /// <summary>The binding every <c>.dgr</c> body is read and written with.</summary>
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

    /// <summary>
    /// The entry of binding type <paramref name="type"/> (<c>Node</c> or <c>Dependency</c>) the module knows
    /// by the id written in the document: the first with it, as the module has always found one.
    /// </summary>
    internal FblElement? Find(string type, string writtenId) =>
        Model.Elements.FirstOrDefault(element => element.Type == type && DependencyGraphReading.TextOf(element, "storedId") == writtenId);

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change) => _edits.Change(change);
}
