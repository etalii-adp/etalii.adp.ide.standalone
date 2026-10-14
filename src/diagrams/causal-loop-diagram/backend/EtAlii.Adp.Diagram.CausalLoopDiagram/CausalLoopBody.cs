using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// One <c>.cld</c> body, read through FBL: the module's binding (<c>causal-loop-diagram.fbl</c>,
/// embedded) applied by <c>EtAlii.Adp.Specification.Fbl</c>, and the DISL model built from that reading
/// under the bundled definition (causal-loop-disl-fbl Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this module reads a statement any more.</b> Which lines are variables, links and
/// loops, and which word of a line holds which attribute, is the binding's; the ids are the
/// definition's natural ids (<see cref="DislNaturalIds"/>), with its prefixes, so a variable is
/// <c>variable:&lt;name&gt;</c>, a link <c>link:&lt;from&gt;|&lt;to&gt;</c> and a loop
/// <c>loop:&lt;identifier&gt;</c>, and the positions a registration stores under those ids apply.
/// </para>
/// <para>
/// <b>Writing is still the module's writer</b> until every edit is a splice (task 7): this body only reads.
/// </para>
/// <para>
/// <b>A text read recently is not read again.</b> Reading is a pure function of the bytes, so
/// <see cref="Parse"/> forks the open body of a text it read lately rather than reading it once more:
/// the menus, the rows, the findings and the store each ask for the same text in turn.
/// </para>
/// </remarks>
public sealed class CausalLoopBody
{
    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    private static readonly Lazy<FblOptions> LoadedOptions = new(() => new FblOptions
    {
        FileName = "body.cld",
        DeriveId = DislNaturalIds.Of(CausalLoopDefinition.Specification),
    });

    private static readonly RecentBodies Recent = new();

    private readonly OpenBody _body;
    private readonly Lazy<DislModel> _disl;

    private CausalLoopBody(OpenBody body)
    {
        _body = body;
        Model = body.Model;
        _disl = new Lazy<DislModel>(() => DislModelBuilder.From(Model, CausalLoopDefinition.Specification));
    }

    /// <summary>
    /// What a new <c>.cld</c> is created with: the binding's template, the <i>Births beget births</i>
    /// starter (finding B9), CRLF, the house style, since a new file has no style of its own to keep.
    /// </summary>
    public static string Template => LoadedBinding.Value.Template?.Text
        ?? throw new InvalidOperationException("The causal loop diagram's FBL binding has no template.");

    /// <summary>The body's text.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Bytes);

    /// <summary>What the binding reads from the body.</summary>
    public FblModel Model { get; }

    /// <summary>The DISL model of what the binding reads: what the menus, the rows and the findings are derived from.</summary>
    public DislModel Disl => _disl.Value;

    /// <summary>A body for <paramref name="text"/>. Never throws on content: a statement the binding does not read is read as unreadable.</summary>
    public static CausalLoopBody Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (Recent.Find(bytes) is { } known) return known;

        var body = new CausalLoopBody(OpenBody.Open(bytes, LoadedBinding.Value, LoadedOptions.Value));
        Recent.Remember(bytes, body);
        return body;
    }

    /// <summary>The statements the binding does not read, each with its text and its 1-based line, in document order.</summary>
    public IReadOnlyList<(string Statement, int Line)> UnreadableStatements =>
    [
        .. Disl.Unreadable.Select(element => (Encoding.UTF8.GetString(_body.Bytes, element.OwnSpan.Start, element.OwnSpan.End - element.OwnSpan.Start).TrimEnd('\r', '\n'), element.Line)),
    ];

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
                        element.Attributes.GetValueOrDefault("weight") is { } weight ? Convert.ToDouble(weight, System.Globalization.CultureInfo.InvariantCulture) : null,
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
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    /// <summary>The bodies of the last few texts read, by their bytes; each is only read, never changed.</summary>
    private sealed class RecentBodies
    {
        private const int Capacity = 8;

        private readonly LinkedList<(byte[] Bytes, CausalLoopBody Body)> _bodies = [];
        private readonly Lock _lock = new();

        public CausalLoopBody? Find(byte[] bytes)
        {
            lock (_lock)
            {
                for (var node = _bodies.First; node is not null; node = node.Next)
                {
                    if (!node.Value.Bytes.AsSpan().SequenceEqual(bytes)) continue;
                    _bodies.Remove(node);
                    _bodies.AddFirst(node);
                    return node.Value.Body;
                }
            }

            return null;
        }

        public void Remember(byte[] bytes, CausalLoopBody body)
        {
            lock (_lock)
            {
                _bodies.AddFirst((bytes, body));
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
