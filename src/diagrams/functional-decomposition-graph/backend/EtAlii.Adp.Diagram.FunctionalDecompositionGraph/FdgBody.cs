using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
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
/// <b>Writing is still <see cref="FdgWriter"/>'s</b> until every edit is written by splices
/// (functional-decomposition-graph-disl-fbl task 10): a command writes a copy of the text with it, and
/// <see cref="Rewrite"/> reads the copy's new text.
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

    private OpenBody _body;
    private FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<IReadOnlyList<Line>> _lines = new();
    private readonly TextMemo<DislModel> _disl = new();

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

    /// <summary>
    /// Makes <paramref name="text"/> this body's text: what <see cref="FdgWriter"/> wrote to a copy of it.
    /// The bridge of task 9, removed when every edit is a <see cref="ModelChange"/> (task 10).
    /// </summary>
    internal void Rewrite(string text)
    {
        _body = Open(text);
        _edits = new FblBodyEdits(_body, Recent);
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
}
