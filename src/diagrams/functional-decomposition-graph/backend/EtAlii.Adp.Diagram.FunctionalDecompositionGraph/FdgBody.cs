using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// One <c>.fdg</c> body, read through FBL: the module's binding (<c>functional-decomposition-graph.fbl</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the one DISL model built from that reading
/// under the bundled definition, which the menus, the rows, the findings and the payloads are derived from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which entries are elements and connections, and what each value reads as, is the binding's.</b>
/// Every value is the text it was written with (FBL 0.5 section 5.2), so a name written <c>1.0</c>,
/// <c>yes</c> or <c>null</c> stays what its author wrote. The slots are read with the attribute types the
/// definition gives them (<see cref="DislAttributeTypes"/>). <see cref="FdgReading"/> puts the reading in
/// the shape the model is built from, with the tool's numbers and ids.
/// </para>
/// <para>
/// <b>Nothing in this module parses or splices YAML.</b> Every write is a <see cref="ModelChange"/> the
/// library plans as splices in the body's own conventions (FBL section 6), so an unchanged document stays
/// byte-identical, and a comment, an unknown key and a blank line survive because no splice touches them.
/// A change of the DISL model is written with the ids the binding gave its entries
/// (<see cref="ForBinding"/>), which differ from the model's where an id is written twice or not at all.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately (<see cref="OpenBody.Fork"/>)
/// rather than reading it once more: the store, the validator and a command each ask for the same text in
/// turn. Each fork is its own body, so an edit to one is never seen by another.
/// </para>
/// </remarks>
public sealed class FdgBody : ITextBody<FdgBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(FdgBody).Assembly, "functional-decomposition-graph.fbl", "fdg"));

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.fdg",
        AttributeType = DislAttributeTypes.Of(FdgDefinition.Specification),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<IReadOnlyList<Line>> _lines = new();
    private readonly TextMemo<DislModel> _disl = new();
    private readonly TextMemo<IReadOnlyDictionary<string, string>> _bindingIds = new();

    private FdgBody(OpenBody body)
    {
        _body = body;
        _edits = new FblBodyEdits(body, Recent);
    }

    /// <summary>The binding every <c>.fdg</c> body is read with.</summary>
    private static FblBinding Binding => LoadedBinding.Value;

    /// <summary>The text of a new, empty <c>.fdg</c> document: the binding's template.</summary>
    public static string Template => Binding.Template?.Text ?? throw new InvalidOperationException("The binding of a .fdg body has no template.");

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's lines as they are now, each with its own ending.</summary>
    internal IReadOnlyList<Line> Lines => _lines.Of(_body.Bytes, () => LineDocument.Parse(Text).Lines);

    /// <summary>What the binding reads from the body now.</summary>
    public FblModel Model => _model.Of(_body.Bytes, () => _body.Model);

    /// <summary>
    /// The DISL model of what the binding reads now (<see cref="DislModelBuilder"/>, under the bundled
    /// definition, from <see cref="FdgReading"/>): what the menus, the rows, the findings and the payloads
    /// are derived from. Read it, never change it.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(FdgReading.Of(Model), FdgDefinition.Specification));

    /// <summary>Whether the body could not be read: it is not YAML, or a mapping in it repeats a key.</summary>
    public bool IsUnreadable => FdgReading.IsUnreadable(Model);

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a body the library cannot read reads as unreadable.</summary>
    public static FdgBody Parse(string text) => new(Open(text));

    private static OpenBody Open(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return known.Fork();

        var body = OpenBody.Open(bytes, LoadedBinding.Value, LoadedOptions.Value);
        Recent.Remember(body.Fork());
        return body;
    }

    /// <summary>The id the binding gave each entry, by the id the DISL model gives it (<see cref="FdgReading.BindingIds"/>).</summary>
    private IReadOnlyDictionary<string, string> BindingIds => _bindingIds.Of(_body.Bytes, () => FdgReading.BindingIds(Model));

    /// <summary>
    /// <paramref name="change"/>, made to the DISL model, as the binding is told it: each entry named by the id the
    /// binding gave it, and a connection's ends by the ids they are written with.
    /// </summary>
    internal ModelChange ForBinding(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change switch
        {
            ModelChange.Set set => set with { Id = BindingIdOf(set.Id) },
            ModelChange.Remove remove => new ModelChange.Remove(BindingIdOf(remove.Id)),
            ModelChange.Add add => add with { Attributes = add.Attributes.ToDictionary(pair => pair.Key, pair => pair.Key is "from" or "to" ? WrittenIdOf(pair.Value) : pair.Value, StringComparer.Ordinal) },
            _ => change,
        };
    }

    private string BindingIdOf(string id) => BindingIds.GetValueOrDefault(id, id);

    private object? WrittenIdOf(object? end) =>
        end is string id && Disl.Diagram.Elements.FirstOrDefault(candidate => candidate.Id == id) is { } element ? FdgDefinition.WrittenId(element) : end;

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change) => _edits.Change(change);

    /// <summary>Plans <paramref name="change"/> against the body as it is now, without applying it.</summary>
    internal PlanResult Plan(ModelChange change) => _body.Plan(change);

    /// <summary>Applies an edit planned against the body as it is now.</summary>
    internal void Apply(Edit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        _body.Apply(edit);
        Recent.Remember(_body.Fork());
    }

    /// <summary>The body's bytes now.</summary>
    internal byte[] Bytes => _body.Bytes;

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
}
