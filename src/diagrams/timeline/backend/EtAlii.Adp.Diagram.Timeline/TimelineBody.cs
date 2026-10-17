using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// One <c>.tml</c> body, read through FBL: the module's binding (<c>timeline.fbl</c>, embedded)
/// applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model built from that reading under
/// the bundled definition (timeline-disl-fbl Requirement 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which entries are periods, moments and relations, and what each value reads as, is the
/// binding's.</b> Every value is the text it was written with (FBL 0.5 section 5.2): a label is a
/// string and reads its written text unasked, and the binding asks for the text of a begin, an end
/// and a row, so <c>yes</c>, <c>1.0</c> and <c>null</c> stay what the author wrote. The slots are
/// read with the attribute types the definition gives them (<see cref="DislAttributeTypes"/>).
/// </para>
/// <para>
/// <b>Every write is a <see cref="ModelChange"/></b> the library plans as splices in the body's own
/// conventions (FBL section 6, timeline-disl-fbl Requirement 3.1): a value's own span in its own style, a
/// new key where the binding's <c>keys</c> put it, a new entry indented as the entries already there. No
/// code here builds a line of YAML, so an unchanged document stays byte-identical and a comment, an
/// unknown key or a blank line survives because no splice touches it.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back
/// (<see cref="TimelineEdits"/>). The library's own history is per open body, and a command opens a
/// fresh one on the store's text each time.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read lately rather than reading it once more:
/// the store, the validator and a command each ask for the same text in turn. Each fork is its own
/// body, so a reading of one is never changed by another.
/// </para>
/// </remarks>
public sealed class TimelineBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(TimelineBody).Assembly, "timeline.fbl", "timeline"));

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.tml",
        AttributeType = DislAttributeTypes.Of(TimelineDefinition.Specification),
        TimeAttributes = DislAttributeTypes.TimeAttributes(TimelineDefinition.Specification, LoadedBinding.Value),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<DislModel> _disl = new();

    private TimelineBody(OpenBody body)
    {
        _body = body;
        _edits = new FblBodyEdits(body, Recent);
    }

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>What the binding reads from the body.</summary>
    public FblModel Model => _model.Of(_body.Bytes, () => _body.Model);

    /// <summary>
    /// The DISL model of what the binding reads (<see cref="DislModelBuilder"/>, under the bundled
    /// definition): what the menus, the rows and the findings are derived from. Read it, never change it.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(Model, TimelineDefinition.Specification));

    /// <summary>A DISL diagram of what the binding reads that is the caller's own: the working diagram an operation changes as it runs.</summary>
    public DislDiagram Working() => DislModelBuilder.From(Model, TimelineDefinition.Specification).Diagram;

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a body the library cannot read reads as unreadable.</summary>
    public static TimelineBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new TimelineBody(known.Fork());

        var body = OpenBody.Open(bytes, LoadedBinding.Value, LoadedOptions.Value);
        Recent.Remember(body.Fork());
        return new TimelineBody(body);
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    public LineRange LinesOf(Span span) => _edits.LinesOf(span);

    /// <summary>
    /// The library's id of the entry whose lines start at <paramref name="range"/>: the element (or, with
    /// <paramref name="relation"/>, the relation) the module's model read there. Found by its place rather
    /// than by its written id, because an id written twice is the library's for its first holder only.
    /// </summary>
    internal string? IdAt(LineRange range, bool relation)
    {
        foreach (var element in Model.Elements)
        {
            if ((element.Rule == "connection") != relation) continue;
            if (LinesOf(element.OwnSpan).Start == range.Start) return element.Id;
        }

        return null;
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change) => _edits.Change(change);

    /// <inheritdoc cref="FblBodyEdits.Batch"/>
    /// <remarks>The caller that promises each write touches its own entry alone: Arrange diagram, which sets one row per entry.</remarks>
    internal void Batch(Action writes) => _edits.Batch(writes);
}
