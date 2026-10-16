using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// One <c>.ghg</c> body, read and written through FBL: the module's binding
/// (<c>gartner-hype-cycle-graph.fbl</c>, embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module parses or splices YAML any more.</b> Reading is the library's reading of
/// the binding's rules; every write is a <see cref="ModelChange"/> the library plans as splices in the
/// body's own conventions (FBL §6), so an unchanged document stays byte-identical and unknown keys,
/// comments and blank lines survive because no splice touches them.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back. The library's own
/// history is per open body, and a command opens a fresh one on the cached text each time.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately (<see cref="OpenBody.Fork"/>)
/// rather than reading it once more: a command's copy of the cached document, the validator's
/// reading of the text the store holds and an undo's restored text all cost no reading. Each fork
/// is its own body, so an edit to one is never seen by another.
/// </para>
/// </remarks>
public sealed class GhgBody : ITextBody<GhgBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(GhgBody).Assembly, "gartner-hype-cycle-graph.fbl", "ghg"));

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<IReadOnlyList<Line>> _lines = new();
    private readonly TextMemo<DislModel> _disl = new();

    private GhgBody(OpenBody body)
    {
        _body = body;
        _edits = new FblBodyEdits(body, Recent);
    }

    /// <summary>The binding every <c>.ghg</c> body is read and written with.</summary>
    private static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's lines as they are now, each with its own ending.</summary>
    public IReadOnlyList<Line> Lines => _lines.Of(_body.Bytes, () => LineDocument.Parse(Text).Lines);

    /// <summary>What the binding reads from the body now.</summary>
    public FblModel Model => _model.Of(_body.Bytes, () => _body.Model);

    /// <summary>
    /// The DISL model of what the binding reads now (<see cref="DislModelBuilder"/>, under the bundled
    /// definition): what the context menus and the property rows are derived from.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(Model, GhgDefinition.Specification));

    /// <summary>A body for <paramref name="text"/>. Never throws on content: an unreadable body reads as such.</summary>
    public static GhgBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new GhgBody(known.Fork());

        var body = OpenBody.Open(bytes, Binding, new FblOptions { FileName = "body.ghg" });
        Recent.Remember(body.Fork());
        return new GhgBody(body);
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    internal LineRange LinesOf(Span span) => _edits.LinesOf(span);

    /// <summary>
    /// The 0-based line of <paramref name="key"/> in the entry on <paramref name="range"/>: the line
    /// that starts with it at the entry's own key indentation, else the entry's first line.
    /// </summary>
    internal int KeyLine(LineRange range, string key)
    {
        var lines = Lines;
        if (range.Start >= lines.Count)
        {
            return range.Start;
        }

        var first = lines[range.Start].Text;
        var indent = first.Length - first.TrimStart(' ', '-').Length;
        for (var index = range.Start; index <= Math.Min(range.End, lines.Count - 1); index++)
        {
            var text = lines[index].Text;
            if (text.Length > indent && text.AsSpan(0, indent).Trim(" -").IsEmpty && text.AsSpan(indent).StartsWith(key + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return range.Start;
    }

    /// <summary>
    /// The element of <paramref name="type"/> the module knows by <paramref name="id"/> and
    /// <paramref name="range"/>: by its stored id when only one entry has it, else by the line it starts on.
    /// </summary>
    private FblElement? Find(string type, string id, LineRange range)
    {
        var typed = Model.Elements.Where(element => element.Type == type).ToList();
        var byId = typed.Where(element => GhgParser.Text(element.Attributes.GetValueOrDefault("storedId")) == id).ToList();
        return byId.Count == 1 ? byId[0] : typed.FirstOrDefault(element => element.Line - 1 == range.Start);
    }

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change) => _edits.Change(change);

    /// <inheritdoc cref="FblBodyEdits.Batch"/>
    /// <remarks>The caller that promises each write touches its own entry alone: Arrange, which rewrites rows bottom-up for exactly that reason.</remarks>
    internal void Batch(Action writes) => _edits.Batch(writes);

    /// <summary>Sets attributes of the element the module knows by <paramref name="id"/> and <paramref name="range"/>.</summary>
    internal EditOutcome Set(string type, string id, LineRange range, IReadOnlyDictionary<string, object?> attributes)
    {
        if (Find(type, id, range) is not { } element)
        {
            return GhgRefusals.Gone;
        }

        return attributes.Count == 0 ? EditOutcome.Applied : Change(new ModelChange.Set(element.Id, attributes));
    }

    /// <summary>Removes the element the module knows by <paramref name="id"/> and <paramref name="range"/>, with what cascades from it.</summary>
    internal EditOutcome Remove(string type, string id, LineRange range) =>
        Find(type, id, range) is { } element ? Change(new ModelChange.Remove(element.Id)) : GhgRefusals.Gone;
}
