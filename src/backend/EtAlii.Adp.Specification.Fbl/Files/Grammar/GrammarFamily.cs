using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Files.Grammar;

/// <summary>
/// The <c>grammar</c> family (FBL 0.9 §4.9): a body read by the grammar its binding carries, built by <c>EtAlii.Lark</c>.
/// Its entries are the nodes and tokens of the tree the grammar gives, addressed by their rules', aliases' and terminals'
/// names; every byte no rule maps is kept as it is.
/// </summary>
internal sealed class GrammarFamily(BodyText text, FblBinding binding, FblOptions options) : FamilyReader(text, binding, options)
{
    private readonly Dictionary<string, IReadOnlyList<GrammarSegment>> _selectors = new(StringComparer.Ordinal);
    private readonly Dictionary<GrammarNode, CelMap> _cel = [];
    private readonly Dictionary<int, Span> _commentLines = [];
    private readonly Dictionary<int, int> _commentEnds = [];
    private List<Entry> _entries = [];
    private List<Span> _leaves = [];
    private GrammarNode? _root;

    public override string FamilyName => "grammar";

    public override IReadOnlyList<Entry> Entries => _entries;

    protected override IReadOnlyList<Span> Leaves => _leaves;

    protected override bool IsTrivia(Span gap) => IsWhitespace(gap);

    private GrammarBuild Grammar => Binding.Body.Grammar?.Built ?? throw new InvalidOperationException($"The binding '{Binding.Name}' has no built grammar.");

    // ---- reading ----

    public override void Parse()
    {
        GrammarReading reading;
        try
        {
            reading = new GrammarParser(Grammar, Text).Parse();
        }
        catch (GrammarSyntaxException e)
        {
            Unreadable = (e.Offset, $"The body does not read by its grammar: {e.Message}");
            return;
        }
        _root = reading.Root;
        _entries = [.. reading.Nodes];
        _leaves = [.. reading.Tokens];
        foreach (var comment in reading.Comments) RememberComment(comment);
        foreach (var entry in _entries) entry.LineSpan = LineSpanOf((GrammarNode)entry);
    }

    private void RememberComment(Span comment)
    {
        _commentEnds[comment.Start] = comment.End;
        if (OnlyWhitespaceBefore(comment.Start) && OnlyCommentsAfter(comment.End)) _commentLines[Text.LineIndexAt(comment.Start)] = comment;
    }

    /// <summary>
    /// The line span (FBL §4.1.1, research R6): the entry's first line to its last, when only whitespace stands before it and
    /// only whitespace or a comment after it, extended upwards over the lines directly above it that hold only a comment.
    /// </summary>
    private Span? LineSpanOf(GrammarNode node)
    {
        if (!node.HasSpan || node.Own.End == node.Own.Start) return null;
        // A node whose last token is a line ending ends its line itself, as a statement of '_NL' does.
        var endsItsLine = Text.Bytes[node.Own.End - 1] == (byte)'\n';
        if (!OnlyWhitespaceBefore(node.Own.Start) || (!endsItsLine && !OnlyCommentsAfter(node.Own.End))) return null;
        var first = Text.LineIndexAt(node.Own.Start);
        var last = Text.LineIndexAt(node.Own.End - 1);
        while (first > 0 && _commentLines.ContainsKey(first - 1)) first--;
        return new Span(Text.Lines[first].Start, Text.Lines[last].End);
    }

    /// <summary>Whether only whitespace and comments follow <paramref name="offset"/> on its line.</summary>
    private bool OnlyCommentsAfter(int offset)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        for (var i = offset; i < line.ContentEnd; i++)
        {
            if (_commentEnds.TryGetValue(i, out var end))
            {
                i = end - 1;
                continue;
            }
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t')) return false;
        }
        return true;
    }

    /// <summary>The bytes of a node's own span: its text (FBL 0.9 §4.9).</summary>
    private string TextOf(GrammarNode node) => Text.Text(node.Own);

    private IReadOnlyList<GrammarSegment> Selector(string selector, bool slot)
    {
        var key = (slot ? "s:" : "a:") + selector;
        if (!_selectors.TryGetValue(key, out var segments))
        {
            segments = GrammarSelector.Parse(selector, slot) ?? throw new InvalidOperationException($"'{selector}' was checked as a selector when the binding loaded.");
            _selectors[key] = segments;
        }
        return segments;
    }

    public override IEnumerable<Candidate> Candidates(Rule rule)
    {
        if (rule.At is null || _root is null) return [];
        return GrammarSelector.Match(_root, Selector(rule.At, slot: false), TextOf).Select(node => new Candidate(rule, null, node, new Dictionary<string, string>()));
    }

    /// <summary>A node as CEL sees it (FBL §4.1.4): its <c>type</c>, <c>text</c> and <c>children</c>, an empty part null and a token's children none.</summary>
    public override object? CelValue(Candidate candidate) => Cel((GrammarNode)candidate.Entry);

    private CelMap? Cel(GrammarNode node)
    {
        if (node.Empty) return null;
        if (_cel.TryGetValue(node, out var map)) return map;
        map = new CelMap
        {
            ["type"] = node.Name,
            ["text"] = TextOf(node),
            ["children"] = node.Parts.Select(object? (part) => Cel(part)).ToList(),
        };
        _cel[node] = map;
        return map;
    }

    public override (string Name, object? Value) CelExtra(Candidate candidate) => ("path", new CelMap());

    /// <summary>
    /// A <c>{node}</c> slot (FBL 0.9 §4.9, §5.2): the text of the first node or token, in document order, its selector
    /// reaches from the entry; with <c>match</c>, the group <c>value</c> of the expression the whole text matches, whose span
    /// is the value's. A text the expression does not match is read-only and absent (research R8), so its bytes are kept.
    /// </summary>
    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        if (slot.Kind is not SlotKind.Node(var selector)) return SlotRead.ReadOnlyAbsent($"A grammar entry has no {slot}.");
        var node = GrammarSelector.Match((GrammarNode)candidate.Entry, Selector(selector, slot: true), TextOf).FirstOrDefault();
        return node is null ? SlotRead.Absent : ReadNode(node, slot.Match);
    }

    private SlotRead ReadNode(GrammarNode node, string? match)
    {
        var text = TextOf(node);
        if (match is null) return new SlotRead(text, node.Own, true, true) { Node = node, Wire = text };
        if (Regex($"^(?:{match})\\z", false).Match(text) is not { } matched) return SlotRead.ReadOnlyAbsent($"'{text}' does not match the slot's match.");
        var value = matched.Groups["value"];
        var start = node.Own.Start + Encoding.UTF8.GetByteCount(text.AsSpan(0, value.Success ? value.Index : 0));
        var span = new Span(start, start + Encoding.UTF8.GetByteCount(value.Value));
        return new SlotRead(value.Value, span, true, true) { Node = node, Wire = value.Value };
    }

    public override SlotRead ReadRaw(Entry entry, string name) =>
        Read(new Candidate(null, null, entry, new Dictionary<string, string>()), new Slot { Kind = new SlotKind.Node(name) });

    public override bool HeaderHolds(HeaderSettings header) =>
        header.Line is null || (Text.Lines.Count > 0 && Regex(header.Line, false).IsMatch(Text.Text(Text.Lines[0].Start, Text.Lines[0].ContentEnd)));

    // ---- writing (FBL §6): task T006 of etalii.adp spec 072 ----

    private const string NotYet = "This host does not write a body of the grammar family yet.";

    public override string Format(SlotRead read, AttributeBinding? binding, object? value) => NewText.Plain(value, binding);

    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change) => false;

    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        Plan.Refuse(NotYet);
        return true;
    }

    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        Plan.Refuse(NotYet);
        return true;
    }

    public override void Insert(Plan plan, InsertRequest request) => Plan.Refuse(NotYet);

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed) => Plan.Refuse(NotYet);
}
