using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// One <c>.cld</c> body, read and written through FBL: the module's binding (<c>causal-loop-diagram.fbl</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model built from that reading
/// under the bundled definition (causal-loop-disl-fbl Requirements 4 and 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module reads or writes a statement any more.</b> Which lines are variables, links
/// and loops, and which word of a line holds which attribute, is the binding's; the ids are the
/// definition's natural ids (<see cref="DislNaturalIds"/>), with its prefixes, so a variable is
/// <c>variable:&lt;name&gt;</c>, a link <c>link:&lt;from&gt;|&lt;to&gt;</c> and a loop
/// <c>loop:&lt;identifier&gt;</c>, and the positions a registration stores under those ids apply. Every
/// write is a <see cref="ModelChange"/> the library plans as splices in the body's own conventions, so an
/// unchanged document stays byte-identical, and comments, blank lines and words no rule binds survive.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the text back.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read or wrote lately rather than reading it once
/// more: the menus, the rows, the findings, the store and a command each ask for the same text in turn.
/// Each fork is its own body, so an edit to one is never seen by another.
/// </para>
/// </remarks>
public sealed class CausalLoopBody : ITextBody<CausalLoopBody>
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(CausalLoopBody).Assembly, "causal-loop-diagram.fbl", "cld"));

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.cld",
        DeriveId = DislNaturalIds.Of(CausalLoopDefinition.Specification),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;

    private readonly TextMemo<FblModel> _model = new();
    private readonly TextMemo<DislModel> _disl = new();

    private CausalLoopBody(OpenBody body)
    {
        _body = body;
    }

    /// <summary>
    /// What a new <c>.cld</c> is created with: the binding's template, the <i>Births beget births</i>
    /// starter (finding B9), CRLF, the house style, since a new file has no style of its own to keep.
    /// </summary>
    public static string Template => LoadedBinding.Value.Template?.Text
        ?? throw new InvalidOperationException("The causal loop diagram's FBL binding has no template.");

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>What the binding reads from the body now, with each link's note as the tool reads it (<see cref="CausalLoopNote"/>).</summary>
    private FblModel Model => _model.Of(_body.Bytes, () => CausalLoopNote.Applied(_body.Model, _body.Bytes));

    /// <summary>
    /// The DISL model of what the binding reads now: what the menus, the rows and the findings are
    /// derived from, and the working diagram an edit's operations and hooks run on.
    /// </summary>
    public DislModel Disl => _disl.Of(_body.Bytes, () => DislModelBuilder.From(Model, CausalLoopDefinition.Specification));

    /// <summary>
    /// A DISL model of what the binding reads now that is the edit's own: the working diagram an
    /// operation's or a hook's actions change as they run, which no reading of this body sees.
    /// </summary>
    public DislDiagram Working() => DislModelBuilder.From(Model, CausalLoopDefinition.Specification).Diagram;

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a statement the binding does not read is read as unreadable.</summary>
    public static CausalLoopBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return new CausalLoopBody(known.Fork());

        var body = OpenBody.Open(bytes, LoadedBinding.Value, LoadedOptions.Value);
        Recent.Remember(body.Fork());
        return new CausalLoopBody(body);
    }

    /// <summary>The statements the binding does not read, each with its text and its 1-based line, in document order.</summary>
    public IReadOnlyList<(string Statement, int Line)> UnreadableStatements =>
    [
        .. Disl.Unreadable.Select(element => (Encoding.UTF8.GetString(_body.Bytes, element.OwnSpan.Start, element.OwnSpan.End - element.OwnSpan.Start).TrimEnd('\r', '\n'), element.Line)),
    ];

    /// <summary>
    /// Plans and applies <paramref name="change"/>, one model change in the binding's terms; the
    /// refusal's sentence when the library refuses it, with nothing written.
    /// </summary>
    /// <remarks>
    /// A link's note that the tool reads where the binding does not (<see cref="CausalLoopNote"/>) is
    /// written over the word the tool read it from, the rest of the change by the library.
    /// </remarks>
    public EditOutcome Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change is ModelChange.Set set && CausalLoopNote.Splices(_body.Model, _body.Bytes, set) is { } note)
        {
            if (note.Edit.Splices.Count > 0) _body.Apply(note.Edit);
            if (note.Remaining.Attributes.Count == 0)
            {
                Recent.Remember(_body.Fork());
                return EditOutcome.Applied;
            }

            change = note.Remaining;
        }

        // A change that re-emits a link's line writes the binding's note, which need not be the tool's.
        var other = change as ModelChange.Set;
        var kept = other is not null && !other.Attributes.ContainsKey(CausalLoopNote.Attribute) ? CausalLoopNote.Read(_body.Model, _body.Bytes, other.Id) : null;
        var result = _body.Change(change);
        if (result is PlanResult.Refused refused) return EditOutcome.Refused(refused.Reason);
        if (other is not null && kept is not null && CausalLoopNote.Read(_body.Model, _body.Bytes, other.Id) != kept)
        {
            return Change(new ModelChange.Set(other.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [CausalLoopNote.Attribute] = kept }));
        }

        Recent.Remember(_body.Fork());
        return EditOutcome.Applied;
    }

    /// <summary>
    /// What the binding reads, as the module's model: the variables, links and loops in document order,
    /// each with the line it is on, and every name as written without its quotes; a loop with what the
    /// definition derives of it, its computed polarity and whether its identifier disagrees with it.
    /// </summary>
    public CausalLoopModel Read()
    {
        var variables = new List<CausalLoopVariable>();
        var links = new List<CausalLoopLink>();
        var loops = new List<CausalLoopLoop>();

        // Every loop the binding reads is one Loop of the DISL model, in the same order.
        using var derived = Disl.Diagram.NodesOfType("Loop").GetEnumerator();
        foreach (var element in Model.Elements)
        {
            var lines = new LineRange(element.Line - 1, element.Line - 1);
            switch (element.Type)
            {
                case "Variable":
                    variables.Add(new CausalLoopVariable(TextOf(element, "name"), TextOf(element, "label"), lines));
                    break;
                case "Link":
                    links.Add(new CausalLoopLink(
                        TextOf(element, "causeName"),
                        TextOf(element, "effectName"),
                        TextOf(element, "polarity") switch
                        {
                            "positive" => CausalLoopPolarity.Positive,
                            "negative" => CausalLoopPolarity.Negative,
                            _ => CausalLoopPolarity.Unstated,
                        },
                        element.Attributes.GetValueOrDefault("delayed") is true,
                        element.Attributes.GetValueOrDefault("flipped") is true,
                        element.Attributes.GetValueOrDefault("weight") is { } weight ? Convert.ToDouble(weight, CultureInfo.InvariantCulture) : null,
                        TextOf(element, "note"),
                        lines));
                    break;
                case "Loop":
                    var loop = derived.MoveNext() ? derived.Current : null;
                    loops.Add(new CausalLoopLoop(
                        TextOf(element, "identifier"),
                        TextOf(element, "title"),
                        element.Attributes.GetValueOrDefault("members") is IEnumerable<object?> members ? [.. members.Select(member => Unquoted(member as string ?? ""))] : [],
                        lines)
                    {
                        Computed = (loop?.ValueOf("computed") as string) switch
                        {
                            "reinforcing" => LoopPolarityResult.Reinforcing,
                            "balancing" => LoopPolarityResult.Balancing,
                            _ => LoopPolarityResult.Undecidable,
                        },
                        Disagrees = loop?.ValueOf("disagrees") is true,
                    });
                    break;
            }
        }

        return new CausalLoopModel(variables, links, loops);
    }

    private static string TextOf(FblElement element, string attribute) =>
        Unquoted(element.Attributes.GetValueOrDefault(attribute) as string ?? "");

    private static string Unquoted(string text) =>
        text is ['"', .., '"'] ? text[1..^1] : text;
}
