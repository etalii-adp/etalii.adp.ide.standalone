using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// One <c>.supply</c> body, read through FBL: the module's binding (<c>supply-chain.fbl</c>, embedded) applied by
/// <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model built from that reading under the bundled definition
/// (supply-chain-disl-fbl Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which entries are groups, nodes and flows, and what each value is written as, is the binding's.</b> Every slot
/// is read as the text it was written with (FBL 0.5 section 5.2, findings B1 and B2), so <c>12</c>, <c>yes</c> and
/// <c>~</c> stay what the author wrote; <see cref="SupplyChainReading"/> turns that text into the module's values and
/// says what will not read, and the DISL model is built by <see cref="DislModelBuilder"/> from the binding's reading
/// with those values.
/// </para>
/// <para>
/// <b>Every write is a <see cref="ModelChange"/></b> the library plans as splices in the body's own conventions (FBL
/// section 6): a value's own span in its own style, a new key where the binding orders it, an entry removed with the
/// comments above it (Q1). Nothing in the module builds a line of YAML; <see cref="SupplyChainEdits"/> writes what a
/// transaction of the definition changes, in the binding's names.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back.
/// </para>
/// <para>
/// <b>A body that is not YAML is read-only</b> (<see cref="Refusal"/>): FBL opens it empty and never writes it.
/// Whether it is YAML is asked of YamlDotNet first, which words the refusal as the module always has and also refuses
/// a mapping that holds one key twice (finding B5, kept by Q4), where FBL binds the first and reads on. FBL 7.5 leaves
/// both to the host's YAML reader; YamlDotNet is used for that question only.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so <see cref="Parse"/> forks
/// the open body of a text it read lately: the store, the validator and a command each ask for the same text in turn.
/// </para>
/// </remarks>
public sealed class SupplyChainBody : ITextBody<SupplyChainBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(SupplyChainBody).Assembly, "supply-chain.fbl", "supply"));

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly FblBodyEdits _edits;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<IReadOnlyList<Line>> _lines = new();
    private readonly TextMemo<(string Message, int Line)?> _refusal = new();
    private readonly TextMemo<ParseOutcome<SupplyChainModel>> _reading = new();
    private readonly TextMemo<DislModel> _disl = new();
    private readonly TextMemo<IReadOnlyDictionary<string, FblElement>> _bindingEntries = new();

    private SupplyChainBody(OpenBody body)
    {
        _body = body;
        _edits = new FblBodyEdits(body, Recent);
    }

    /// <summary>The text of a new, empty <c>.supply</c> document: the binding's template.</summary>
    public static string Template => LoadedBinding.Value.Template?.Text ?? "";

    /// <summary>The body's text.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>The body's lines, each with its own ending.</summary>
    private IReadOnlyList<Line> Lines => _lines.Of(_body.Bytes, () => LineDocument.Parse(Text).Lines);

    /// <summary>What the binding reads from the body.</summary>
    public FblModel Model => _model.Of(_body.Bytes, () => _body.Model);

    /// <summary>
    /// Why the body cannot be read, in the YAML reader's words, and the 0-based line it stopped at; null for a body
    /// that reads. A body with a refusal opens empty and is never edited.
    /// </summary>
    public (string Message, int Line)? Refusal => _refusal.Of(_body.Bytes, ReadRefusal);

    /// <summary>What the module reads from the body: its model, the sentences of what will not read, and why it cannot be read at all.</summary>
    public ParseOutcome<SupplyChainModel> Reading => _reading.Of(_body.Bytes, () => SupplyChainReading.Read(this));

    /// <summary>
    /// The DISL model of what the binding reads (<see cref="DislModelBuilder"/>, under the bundled definition), with
    /// each value as the module reads it: what the menus, the rows and the findings are derived from. Read it, never
    /// change it; <see cref="Working"/> gives one to change.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, Working);

    /// <summary>A DISL model of the body of its own, for an edit to run over and change.</summary>
    public DislModel Working() => DislModelBuilder.From(SupplyChainReading.Values(this), SupplyChainDefinition.Specification, new DislEnv());

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a body that cannot be read reads as such.</summary>
    public static SupplyChainBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new SupplyChainBody(known.Fork());

        var body = OpenBody.Open(bytes, LoadedBinding.Value, new FblOptions { FileName = "body.supply" });
        Recent.Remember(body.Fork());
        return new SupplyChainBody(body);
    }

    /// <summary>The 0-based lines <paramref name="span"/> covers.</summary>
    internal LineRange LinesOf(Span span) => _edits.LinesOf(span);

    /// <summary>
    /// The entry the binding reads each group, node and flow as, by the id the DISL model reads it with: its id is the
    /// same but for a later holder of an id and an entry without one, which the model reads with an ephemeral id and the
    /// binding addresses by its place.
    /// </summary>
    internal IReadOnlyDictionary<string, FblElement> BindingEntries => _bindingEntries.Of(_body.Bytes, () => SupplyChainReading.BindingEntries(this));

    /// <summary>The spans <paramref name="change"/> would write, planned against the body as it is; none when it is refused.</summary>
    internal IReadOnlyList<Span> Planned(ModelChange change) =>
        _body.Plan(change) is PlanResult.Planned { Edit: var edit } ? [.. edit.Splices.Select(splice => new Span(splice.Start, splice.End))] : [];

    /// <summary>Plans and applies one change; the refusal's sentence when the library refuses it.</summary>
    internal EditOutcome Change(ModelChange change) => _edits.Change(change);

    /// <inheritdoc cref="FblBodyEdits.Batch"/>
    internal void Batch(Action writes) => _edits.Batch(writes);

    /// <summary>
    /// The 0-based line of <paramref name="key"/> in the mapping on <paramref name="range"/>: the line that starts
    /// with it, plain or quoted, at the mapping's own key indentation; else the mapping's first line, which is where
    /// every key of an entry written on one line is.
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
            if (text.Length > indent && text.AsSpan(0, indent).Trim(" -").IsEmpty && StartsWithKey(text.AsSpan(indent), key))
            {
                return index;
            }
        }

        return range.Start;
    }

    private static bool StartsWithKey(ReadOnlySpan<char> text, string key)
    {
        foreach (var quote in (ReadOnlySpan<string>)["", "\"", "'"])
        {
            if (text.StartsWith(quote + key + quote, StringComparison.Ordinal) && text[(key.Length + (2 * quote.Length))..].TrimStart(' ').StartsWith(":", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>YamlDotNet's refusal first, else the library's unreadable body; null for a body that reads.</summary>
    private (string Message, int Line)? ReadRefusal()
    {
        try
        {
            using var reader = new StringReader(Text);
            new YamlStream().Load(reader);
        }
        catch (YamlException exception)
        {
            return (exception.Message, Math.Max(0, (int)exception.Start.Line - 1));
        }

        if (!Model.Unreadable)
        {
            return null;
        }

        const string leadIn = "The body is not well-formed YAML: ";
        var finding = Model.Findings.FirstOrDefault(finding => finding.Code == FindingCodes.Unparseable);
        var message = finding?.Message ?? "the binding cannot read it.";
        return (message.StartsWith(leadIn, StringComparison.Ordinal) ? message[leadIn.Length..] : message, Math.Max(0, (finding?.Location?.Line ?? 1) - 1));
    }
}
