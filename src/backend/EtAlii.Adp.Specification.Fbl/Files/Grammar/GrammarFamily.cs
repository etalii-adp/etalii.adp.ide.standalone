using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

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
    private int _skipped;

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
        foreach (var skipped in reading.Skipped) ReportSkipped(skipped);
        _skipped = reading.Skipped.Count;
    }

    /// <summary>Skipped text, reported once where reading stopped, with the length of the text skipped from there (FBL 0.9 §4.9, §7.4).</summary>
    private void ReportSkipped(GrammarSkipped skipped)
    {
        var unit = Binding.Body.Grammar?.Recover?.Unit;
        Report(FindingCodes.UnparsedText, FindingSeverity.Warning, $"This text does not read as a {unit} of the grammar; it is kept as it is and binds nothing.", new Span(skipped.Stop, skipped.Text.End));
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
        if (slot.Split is not null) return ReadItems(candidate, slot, selector);
        var node = GrammarSelector.Match((GrammarNode)candidate.Entry, Selector(selector, slot: true), TextOf).FirstOrDefault();
        return node is null ? SlotRead.Absent : ReadNode(node, slot);
    }

    /// <summary>
    /// A <c>{node}</c> slot of a <c>many</c> attribute (FBL 0.11 §5.2): every node its selector reaches, in document order,
    /// each one item, or, with <c>split</c>, each holding the items its value is cut into (FBL 0.13 §4.9).
    /// </summary>
    public override SlotRead ReadMany(Candidate candidate, Slot slot) =>
        slot.Kind is SlotKind.Node(var selector) ? ReadItems(candidate, slot, selector) : Read(candidate, slot);

    /// <summary>
    /// The items of a slot (FBL 0.13 §4.9): with <c>split</c>, the slot is present even where it reaches no node or no
    /// item, as a list holds no item; a node whose text its <c>match</c> does not match holds none, and makes the slot read-only.
    /// </summary>
    private SlotRead ReadItems(Candidate candidate, Slot slot, string selector)
    {
        var values = new List<(GrammarNode Node, Span Span)>();
        var items = new List<GrammarItem>();
        string? reason = null;
        foreach (var node in GrammarSelector.Match((GrammarNode)candidate.Entry, Selector(selector, slot: true), TextOf))
        {
            var read = ReadNode(node, slot);
            if (read is not { Present: true, Span: { } span, Wire: { } wire })
            {
                reason ??= read.Reason;
                continue;
            }
            values.Add((node, span));
            if (slot.Split is not { } split) items.Add(new GrammarItem((string)read.Value!, span, node));
            else items.AddRange(SplitItems.Cut(wire, span.Start, split).Select(item => new GrammarItem(slot.Escape?.Read(item.Text) ?? item.Text, item.Span, node)));
        }
        var list = items.Select(object? (item) => item.Value).ToList();
        return new SlotRead(list, null, slot.Split is not null || items.Count > 0, reason is null, reason) { Node = new GrammarItems(values, items) };
    }

    /// <summary>A node's value: its text, or the group <c>value</c> of the slot's <c>match</c>, its escapes resolved by the slot's <c>escape</c> (FBL 0.11 §5.2), the text as written its wire.</summary>
    private SlotRead ReadNode(GrammarNode node, Slot slot)
    {
        var text = TextOf(node);
        if (slot.Match is not { } match) return new SlotRead(slot.Escape?.Read(text) ?? text, node.Own, true, true) { Node = node, Wire = text };
        if (Regex($"^(?:{match})\\z", false).Match(text) is not { } matched) return SlotRead.ReadOnlyAbsent($"'{text}' does not match the slot's match.");
        var value = matched.Groups["value"];
        var start = node.Own.Start + Encoding.UTF8.GetByteCount(text.AsSpan(0, value.Success ? value.Index : 0));
        var span = new Span(start, start + Encoding.UTF8.GetByteCount(value.Value));
        return new SlotRead(slot.Escape?.Read(value.Value) ?? value.Value, span, true, true) { Node = node, Wire = value.Value };
    }

    public override SlotRead ReadRaw(Entry entry, string name) =>
        Read(new Candidate(null, null, entry, new Dictionary<string, string>()), new Slot { Kind = new SlotKind.Node(name) });

    public override bool HeaderHolds(HeaderSettings header) =>
        header.Line is null || (Text.Lines.Count > 0 && Regex(header.Line, false).IsMatch(Text.Text(Text.Lines[0].Start, Text.Lines[0].ContentEnd)));

    // ---- writing (FBL 0.9 §4.9, §6.3) ----

    /// <summary>
    /// A value as the family writes it: as text, through the binding's <c>map</c> when it has one, numbers and booleans as
    /// FBL §6.3 writes them, escaped by the slot's <c>escape</c> (FBL 0.11 §6.3); a list as its items joined by the slot's
    /// <c>separator</c>, <c>", "</c> when it gives none (FBL 0.13 §6.3).
    /// </summary>
    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        if (value is IEnumerable<object?> list) return string.Join(Separator(binding), list.Select(item => Escaped(binding, NewText.Plain(item, binding))));
        return (binding is null ? null : NewText.Wire(binding, value, read.Wire)) ?? Escaped(binding, NewText.Plain(value, binding));
    }

    private static string Escaped(Slot? slot, string text) => slot?.Escape?.Write(text) ?? text;

    private static string Separator(Slot? slot) => slot?.Separator ?? ", ";

    /// <summary>The family removes no part of an entry (FBL 0.9 §4.9, <i>Writing</i>): an emptied value is set, as any other.</summary>
    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change) => false;

    /// <summary>
    /// A changed value: a <c>replace-value</c> of the value's span, the group <c>value</c> of the slot's <c>match</c> or the
    /// node's own span; a list of items, the difference (<see cref="WriteItems"/>), unless the slot reaches no node.
    /// </summary>
    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        if (change.Read.Node is GrammarItems items)
        {
            if (items.Values.Count == 0) return false;
            WriteItems(plan, change, items);
            return true;
        }
        if (change.Read.Span is not { } span) return false;
        plan.Add(SpliceOperation.ReplaceValue, span, Format(change.Read, change.Binding, change.Value));
        return true;
    }

    /// <summary>
    /// An attribute whose node is absent (FBL 0.11 §5.2, <i>An absent part</i>): written by its binding's <c>create</c>, its
    /// <c>emit</c> by one <c>insert-key</c> right after the last token of the first attribute of <c>after</c> that is
    /// present, the items of a list joined by the slot's separator, or each emitted in turn without <c>split</c>; refused
    /// without <c>create.after</c> or when no attribute it lists is present. A list set to no item writes nothing.
    /// </summary>
    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Read.Node is GrammarItems && NewText.IsEmpty(change.Value)) return true;
        if (change.Binding.Create is not { After: { } after } create || PartEnd(element, after) is not { } offset)
        {
            Plan.Refuse($"This {element.Rule.Type} has no {change.Attribute} in this file to set, and a body read by its grammar is given no part an entry lacks.");
            return true;
        }
        var values = change.Read.Node is GrammarItems && change.Binding.Split is null ? Wanted(change).Select(object? (value) => value).ToList() : [change.Value];
        var text = string.Concat(values.Select(value => NewText.Render(create.Emit, name => name == "value" ? Format(change.Read, change.Binding, value) : null)));
        plan.Add(SpliceOperation.InsertKey, offset, offset, text);
        return true;
    }

    /// <summary>The end of the last token of the first attribute of <paramref name="after"/> the entry holds a part of.</summary>
    private static int? PartEnd(ReadElement element, IReadOnlyList<string> after)
    {
        foreach (var name in after)
        {
            if (!element.Slots.TryGetValue(name, out var read) || !read.Present) continue;
            if (read.Node is GrammarNode node) return node.Own.End;
            if (read.Node is GrammarItems { Values.Count: > 0 } items) return items.Values[^1].Node.Own.End;
        }
        return null;
    }

    /// <summary>The items a set gives a list, as text.</summary>
    private static List<string> Wanted(SlotChange change) => (change.Value as IEnumerable<object?> ?? []).Select(value => NewText.Plain(value, null)).ToList();

    /// <summary>
    /// A set of a list of items (FBL 0.11 §5.2, FBL 0.13 §6.3): the items of the longest common subsequence of the old list
    /// and the new one stay, an item removed and one added at the same place are a <c>replace-value</c> of the item's span,
    /// removed items are written off by <see cref="RemoveItems"/> and new ones by <see cref="AddItems"/>, so that every byte
    /// of an item the set keeps, and between two items it keeps, stays.
    /// </summary>
    private void WriteItems(Plan plan, SlotChange change, GrammarItems items)
    {
        var wanted = Wanted(change);
        var held = items.Items.Select(item => item.Value).ToList();
        var kept = ListDifference.CommonSubsequence(held, wanted);
        var survives = new bool[held.Count];
        var added = new List<string>();
        int i = 0, j = 0;
        foreach ((int Held, int Wanted) anchor in kept.Append((held.Count, wanted.Count)))
        {
            var paired = Math.Min(anchor.Held - i, anchor.Wanted - j);
            for (var k = 0; k < paired; k++)
            {
                survives[i + k] = true;
                plan.Add(SpliceOperation.ReplaceValue, items.Items[i + k].Span, Escaped(change.Binding, wanted[j + k]));
            }
            added.AddRange(wanted.Skip(j + paired).Take(anchor.Wanted - j - paired));
            if (anchor.Held < held.Count) survives[anchor.Held] = true;
            i = anchor.Held + 1;
            j = anchor.Wanted + 1;
        }
        RemoveItems(plan, items, survives);
        if (added.Count > 0) AddItems(plan, change, items, survives, added);
    }

    /// <summary>
    /// The removed items, node by node (FBL 0.13 §6.3): a run after an item the set keeps goes from the end of that item to
    /// the end of the run, a run at the start of a node to the start of the item kept after it, and a node whose every item
    /// goes is removed with the whitespace before it, as a node of a <c>many</c> slot without <c>split</c> is.
    /// </summary>
    private void RemoveItems(Plan plan, GrammarItems items, bool[] survives)
    {
        foreach (var node in items.Values.Select(value => value.Node))
        {
            var places = Enumerable.Range(0, survives.Length).Where(place => items.Items[place].Node == node).ToList();
            if (places.Count == 0) continue;
            if (places.All(place => !survives[place]))
            {
                plan.Add(SpliceOperation.RemoveKey, SpaceBefore(node.Own.Start), node.Own.End, "");
                continue;
            }
            for (var at = 0; at < places.Count; at++)
            {
                if (survives[places[at]]) continue;
                var last = at;
                while (last + 1 < places.Count && !survives[places[last + 1]]) last++;
                var run = (First: items.Items[places[at]].Span, Last: items.Items[places[last]].Span);
                if (at > 0) plan.Add(SpliceOperation.RemoveKey, items.Items[places[at - 1]].Span.End, run.Last.End, "");
                else plan.Add(SpliceOperation.RemoveKey, run.First.Start, items.Items[places[last + 1]].Span.Start, "");
                at = last;
            }
        }
    }

    /// <summary>
    /// The new items (FBL 0.13 §6.3): with <c>split</c>, an <c>insert-key</c> of the separator and the values right after the
    /// last item the set keeps, or, where no node holds an item, a <c>replace-value</c> of the last node's value; without it,
    /// each new node written by the slot's <c>create</c> right after the last node the set keeps (FBL 0.12 §4.9).
    /// </summary>
    private void AddItems(Plan plan, SlotChange change, GrammarItems items, bool[] survives, List<string> added)
    {
        var last = Enumerable.Range(0, survives.Length).Where(place => survives[place]).Select(place => items.Items[place]).LastOrDefault();
        var separator = Separator(change.Binding);
        var joined = string.Join(separator, added.Select(value => Escaped(change.Binding, value)));
        if (change.Binding.Split is not null)
        {
            if (last is not null) plan.Add(SpliceOperation.InsertKey, last.Span.End, last.Span.End, separator + joined);
            else plan.Add(SpliceOperation.ReplaceValue, items.Values[^1].Span, joined);
            return;
        }
        if (change.Binding.Create is not { } create)
        {
            Plan.Refuse($"The binding gives no form to write a new {change.Attribute} in.");
            return;
        }
        var offset = (last?.Node ?? items.Values[^1].Node).Own.End;
        var text = string.Concat(added.Select(value => NewText.Render(create.Emit, name => name == "value" ? Escaped(change.Binding, value) : null)));
        plan.Add(SpliceOperation.InsertKey, offset, offset, text);
    }

    /// <summary>The start of the spaces and tabs that stand right before <paramref name="offset"/>.</summary>
    private int SpaceBefore(int offset)
    {
        while (offset > Text.BomLength && Text.Bytes[offset - 1] is (byte)' ' or (byte)'\t') offset--;
        return offset;
    }

    /// <summary>
    /// The read-again check (FBL 0.9 §4.9, research R12): the body with <paramref name="planned"/> applied reads with no
    /// more text that does not parse, holds as many entries of the rule, and the entry at the changed one's place reads
    /// every changed value as it was written.
    /// </summary>
    protected override bool ReadsBack(IReadOnlyList<Splice> planned, ReadElement element, IReadOnlyList<SlotChange> changes)
    {
        var before = Candidates(element.Rule).Select(candidate => candidate.Entry).ToList();
        var place = before.IndexOf(element.Entry);
        if (place < 0 || ReadAgain(planned) is not { } after) return false;
        var again = after.Candidates(element.Rule).ToList();
        return again.Count == before.Count && changes.All(change => ReadsAgain(after, again[place], change));
    }

    /// <summary>Whether <paramref name="after"/> reads the changed value as it was written: a list holding the new items, in any order, and any other value as written.</summary>
    private bool ReadsAgain(GrammarFamily after, Candidate candidate, SlotChange change)
    {
        if (change.Read.Node is not GrammarItems) return after.Read(candidate, change.Binding) is { Present: true } read && read.Wire == Format(change.Read, change.Binding, change.Value);
        var items = (after.ReadMany(candidate, change.Binding).Value as IEnumerable<object?> ?? []).Select(value => NewText.Plain(value, null));
        return items.Order(StringComparer.Ordinal).SequenceEqual(Wanted(change).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>A set that would not read back is refused, and nothing is written (FBL 0.9 §4.9, §6.4).</summary>
    protected override void Rewrite(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes) =>
        Plan.Refuse($"The new {string.Join(" and ", changes.Select(change => change.Attribute))} would not read back from the text.");

    /// <summary>
    /// A new entry (FBL 0.9 §6.3): its rule's <c>emit</c>, each line feed written as the body's line ending and the new
    /// entry's indentation, placed by <c>insert.place</c> in the container <c>insert.container</c> selects, the root when it
    /// is absent, and read again before it is planned: the body then holds one more entry of the rule, with the add's values.
    /// </summary>
    public override void Insert(Plan plan, InsertRequest request)
    {
        var rule = request.Rule;
        if (rule.Insert?.Emit is not { } emit || _root is null)
        {
            Plan.Refuse($"The binding gives no form to write a new {rule.Type} in.");
            return;
        }
        var container = Container(rule);
        var text = NewText.Render(emit, name => Placeholder(request, name));
        (int offset, string written) = Place(plan, request, container, text);
        var count = Candidates(rule).Count();
        var splice = new Splice(SpliceOperation.InsertEntry, offset, offset, written);
        if (ReadAgain([splice]) is not { } after || !HoldsTheNewEntry(after, request, emit, count, new Span(offset, offset + Encoding.UTF8.GetByteCount(written))))
        {
            Plan.Refuse($"The new {rule.Type} would not read back from the text.");
            return;
        }
        plan.Add(SpliceOperation.InsertEntry, offset, offset, written);
    }

    /// <summary>A removed entry: a <c>remove-entry</c> of its line span, its leading comments included, or of its own span when it does not start and end its lines.</summary>
    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed) =>
        plan.Add(SpliceOperation.RemoveEntry, element.Entry.RemovalSpan, "");

    private GrammarNode Container(Rule rule)
    {
        if (rule.Insert?.Container is not { } selector) return _root!;
        var container = GrammarSelector.Match(_root!, Selector(selector, slot: false), TextOf).FirstOrDefault();
        if (container is null) Plan.Refuse($"The file has no {selector} to add the {rule.Type} to.");
        return container!;
    }

    private string? Placeholder(InsertRequest request, string name) => name switch
    {
        "id" => request.Id,
        "source" => request.Source?.Key,
        "target" => request.Target?.Key,
        _ => request.Values.GetValueOrDefault(name) is { } value ? Format(SlotRead.Absent, request.Rule.Attribute(name), value) : null,
    };

    /// <summary>Where a new entry goes, and the text written there (FBL 0.9 §6.3, <i>Where it goes</i>).</summary>
    private (int Offset, string Text) Place(Plan plan, InsertRequest request, GrammarNode container, string text)
    {
        var children = container.Children.OfType<GrammarNode>().Where(child => child.HasSpan && child.Own.End > child.Own.Start).ToList();
        var ofRule = plan.Reading.Elements.Where(element => element.Rule == request.Rule && Within(element.Entry, container)).Select(element => (GrammarNode)element.Entry).LastOrDefault();
        if (request.After?.Entry is GrammarNode anchor) return After(anchor, text);
        var place = request.Rule.Insert!.Place;
        GrammarNode? followed = place switch
        {
            InsertPlace.AfterLast => ofRule ?? children.LastOrDefault(),
            InsertPlace.NextSibling => ofRule ?? children.LastOrDefault(),
            InsertPlace.End => children.LastOrDefault(),
            InsertPlace.Start or InsertPlace.EndOfDocument => null,
            _ => Refused(place),
        };
        if (followed is not null) return After(followed, text);
        if (place == InsertPlace.Start && children.FirstOrDefault() is { } first) return Before(first, text);
        return AtEndOfBody(text);
    }

    private GrammarNode? Refused(InsertPlace place)
    {
        Plan.Refuse($"A {FamilyName} body cannot place a new entry '{WireName.Of(place)}'.");
        return null;
    }

    private static bool Within(Entry entry, GrammarNode container)
    {
        for (var at = entry.Parent; at is not null; at = at.Parent)
        {
            if (at == container) return true;
        }
        return false;
    }

    /// <summary>
    /// A new entry after <paramref name="entry"/>: after its line span, on a line of its own with its indentation and a
    /// line ending; when it does not end its line, right after its own span, after a line ending and the indentation of
    /// the line it ends on.
    /// </summary>
    private (int Offset, string Text) After(GrammarNode entry, string text)
    {
        if (entry.LineSpan is { } line)
        {
            var indent = IndentationOf(entry.Own.Start);
            var newline = NewlineAt(line.End);
            var lines = Indented(text, indent, newline);
            return line.End == Text.Length && !EndsWithLineEnding() ? (line.End, newline + indent + lines) : (line.End, indent + lines + newline);
        }
        var lineIndent = IndentationOf(Text.Lines[Text.LineIndexAt(Math.Max(entry.Own.Start, entry.Own.End - 1))].ContentEnd);
        var ending = NewlineAt(entry.Own.End);
        return (entry.Own.End, ending + lineIndent + Indented(text, lineIndent, ending));
    }

    /// <summary>A new entry before <paramref name="entry"/>: at the start of its line span, with its indentation, followed by a line ending.</summary>
    private (int Offset, string Text) Before(GrammarNode entry, string text)
    {
        var indent = IndentationOf(entry.Own.Start);
        var start = entry.LineSpan?.Start ?? entry.Own.Start;
        var newline = NewlineAt(start);
        return (start, indent + Indented(text, indent, newline) + newline);
    }

    /// <summary>A new entry after the last byte of the body: on a line of its own when the body is empty or ends a line, else after a line ending.</summary>
    private (int Offset, string Text) AtEndOfBody(string text)
    {
        var end = Text.Length;
        var newline = NewlineAt(end);
        var line = Indented(text, "", newline);
        return end == Text.BomLength || EndsWithLineEnding() ? (end, line + newline) : (end, newline + line);
    }

    private bool EndsWithLineEnding() => Text.Length > Text.BomLength && Text.Bytes[Text.Length - 1] == (byte)'\n';

    /// <summary>The whitespace that starts the line <paramref name="offset"/> stands on, up to it.</summary>
    private string IndentationOf(int offset)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        var start = Math.Max(line.Start, Text.BomLength);
        var end = start;
        while (end < offset && Text.Bytes[end] is (byte)' ' or (byte)'\t') end++;
        return Text.Text(start, end);
    }

    /// <summary><paramref name="text"/> with each line feed written as <paramref name="newline"/> and <paramref name="indent"/>.</summary>
    private static string Indented(string text, string indent, string newline) => text.Replace("\n", newline + indent, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="after"/> holds one more entry of the rule than now, one of them in <paramref name="written"/>, reading every value the add placed in it.</summary>
    private static bool HoldsTheNewEntry(GrammarFamily after, InsertRequest request, string emit, int count, Span written)
    {
        var rule = request.Rule;
        var candidates = after.Candidates(rule).ToList();
        if (candidates.Count != count + 1) return false;
        var placed = NewText.Parts(emit).Select(part => part.Placeholder).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return candidates.Any(candidate => written.Start <= candidate.Entry.Own.Start && candidate.Entry.Own.Start < written.End
            && (!placed.Contains("id") || request.Id is null || rule.Id?.From is not { } from || after.Read(candidate, from).Wire == request.Id)
            && request.Values.All(pair => !placed.Contains(pair.Key) || pair.Value is null or IEnumerable<object?> || rule.Attribute(pair.Key) is not { Kind: SlotKind.Node } binding
                || after.Read(candidate, binding).Wire == after.Format(SlotRead.Absent, binding, pair.Value)));
    }

    /// <summary>The family's reading of the body with <paramref name="splices"/> applied, when it reads and no more of it is skipped than now.</summary>
    private GrammarFamily? ReadAgain(IReadOnlyList<Splice> splices)
    {
        var ordered = splices.Select((splice, index) => (splice, index)).OrderBy(pair => pair.splice.Start).ThenBy(pair => pair.index).Select(pair => pair.splice).ToList();
        var after = new GrammarFamily(new BodyText(Edit.Apply(Text.Bytes, ordered)), Binding, Options);
        after.Parse();
        return after.Unreadable is null && after._skipped <= _skipped ? after : null;
    }
}
