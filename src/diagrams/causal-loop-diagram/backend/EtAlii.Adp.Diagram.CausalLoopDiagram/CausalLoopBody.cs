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
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.cld",
        DeriveId = DislNaturalIds.Of(CausalLoopDefinition.Specification),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;

    // Each read is cached with the bytes it was read from, so a change makes it stale by itself.
    private (byte[] Bytes, FblModel Model)? _model;
    private (byte[] Bytes, DislModel Model)? _disl;

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
    private FblModel Model
    {
        get
        {
            if (_model is not { } model || model.Bytes != _body.Bytes)
            {
                _model = model = (_body.Bytes, CausalLoopNote.Applied(_body.Model, _body.Bytes));
            }

            return model.Model;
        }
    }

    /// <summary>
    /// The DISL model of what the binding reads now: what the menus, the rows and the findings are
    /// derived from, and the working diagram an edit's operations and hooks run on.
    /// </summary>
    public DislModel Disl
    {
        get
        {
            if (_disl is not { } model || model.Bytes != _body.Bytes)
            {
                _disl = model = (_body.Bytes, DislModelBuilder.From(Model, CausalLoopDefinition.Specification));
            }

            return model.Model;
        }
    }

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

        var result = _body.Change(change);
        if (result is PlanResult.Refused refused) return EditOutcome.Refused(refused.Reason);
        Recent.Remember(_body.Fork());
        return EditOutcome.Applied;
    }

    /// <summary>
    /// What the binding reads, as the module's model: the variables, links and loops in document order,
    /// each with the line it is on, and every name as written without its quotes.
    /// </summary>
    public CausalLoopModel Read()
    {
        var variables = new List<CausalLoopVariable>();
        var links = new List<CausalLoopLink>();
        var loops = new List<CausalLoopLoop>();
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
                    loops.Add(new CausalLoopLoop(
                        TextOf(element, "identifier"),
                        TextOf(element, "title"),
                        element.Attributes.GetValueOrDefault("members") is IEnumerable<object?> members ? [.. members.Select(member => Unquoted(member as string ?? ""))] : [],
                        lines));
                    break;
            }
        }

        return new CausalLoopModel(variables, links, loops);
    }

    private static string TextOf(FblElement element, string attribute) =>
        Unquoted(element.Attributes.GetValueOrDefault(attribute) as string ?? "");

    private static string Unquoted(string text) =>
        text is ['"', .., '"'] ? text[1..^1] : text;

    /// <summary>The open bodies of the last few texts read or written, by their bytes: forks no one edits, only forked again.</summary>
    private sealed class RecentBodies
    {
        private const int Capacity = 8;

        private readonly LinkedList<OpenBody> _bodies = [];
        private readonly Lock _lock = new();

        public OpenBody? Find(byte[] bytes)
        {
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (!node.Value.Bytes.AsSpan().SequenceEqual(bytes)) continue;
                    _bodies.Remove(node);
                    _bodies.AddFirst(node);
                    return node.Value;
                }
            }

            return null;
        }

        public void Remember(OpenBody body)
        {
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (!node.Value.Bytes.AsSpan().SequenceEqual(body.Bytes)) continue;
                    _bodies.Remove(node);
                    break;
                }

                _bodies.AddFirst(body);
                while (_bodies.Count > Capacity)
                {
                    _bodies.RemoveLast();
                }
            }
        }
    }

    private static FblBinding LoadBinding()
    {
        var assembly = typeof(CausalLoopBody).Assembly;
        using var stream = assembly.GetManifestResourceStream("causal-loop-diagram.fbl")
            ?? throw new InvalidOperationException("The causal loop diagram's FBL binding is not embedded in this module.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document is null
            ? throw new InvalidOperationException($"The causal loop diagram's FBL binding does not load: {string.Join("; ", problems)}")
            : document.Bindings["cld"];
    }
}
