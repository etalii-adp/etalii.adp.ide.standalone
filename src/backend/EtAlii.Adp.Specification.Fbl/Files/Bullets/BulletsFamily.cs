using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Lines;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Bullets;

/// <summary>
/// The <c>bullets</c> family (FBL 0.7 §4.8, §6.3): the list items under the headings a binding names, read
/// as a tree, and the statements outside every item, read as the <c>lines</c> family reads its own. An item's
/// text is matched and written as a statement is, so the groups, the words and re-emitting are those of
/// <see cref="LinesFamily"/>; what is the family's own is its kinds of line, its sections, the continuation,
/// and where a new or moved item goes.
/// </summary>
internal sealed class BulletsFamily(BodyText text, FblBinding binding, FblOptions options) : LinesFamily(text, binding, options, blocks: false)
{
    private List<Statement> _entries = [];
    private List<Span> _leaves = [];
    private readonly Dictionary<string, (int First, int End)> _sections = new(StringComparer.Ordinal);

    public override string FamilyName => "bullets";

    public override IReadOnlyList<Entry> Entries => _entries;

    protected override IReadOnlyList<Span> Leaves => _leaves;

    public override bool PlacesByPosition => true;

    public override void Parse()
    {
        var parser = new BulletsParser(Text);
        parser.Parse();
        _entries = parser.Entries;
        _leaves = parser.Leaves;
        foreach (var section in Binding.Sections)
        {
            var regex = Regex(section.Heading, section.CaseInsensitive);
            var first = parser.Headings.FirstOrDefault(heading => regex.IsMatch(heading.Text));
            if (first is null) continue;
            var end = parser.Headings.FirstOrDefault(heading => heading.Line > first.Line && heading.Level <= first.Level)?.Line ?? Text.Lines.Count;
            _sections[section.Name] = (first.Line, end);
        }
    }

    /// <summary>Whether <paramref name="item"/> stands in the section <paramref name="name"/>: its item line is one of the section's lines.</summary>
    private bool StandsIn(Bullet item, string name) =>
        _sections.TryGetValue(name, out var section) && section.First < item.FirstLine && item.FirstLine < section.End;

    /// <summary>Line 1, after a byte-order mark, must match the header (FBL 0.7 §5.6); when it is a statement, no rule reads it.</summary>
    public override bool HeaderHolds(HeaderSettings header)
    {
        if (header.Line is null) return true;
        var line = Text.Lines[0];
        var first = Text.BomLength;
        while (first < line.ContentEnd && Text.Bytes[first] is (byte)' ' or (byte)'\t') first++;
        var content = Text.Text(first, line.ContentEnd);
        if (Regex(header.Line, false).Match(content) is null) return false;
        if (_entries.FirstOrDefault(entry => entry.FirstLine == 0 && entry is not Bullet) is { } statement) statement.IsHeader = true;
        return true;
    }

    /// <summary>A rule with <c>within</c> matches the items of the sections it names; one without matches statements.</summary>
    public override IEnumerable<Candidate> Candidates(Rule rule)
    {
        if (rule.Line is null) yield break;
        foreach (var entry in _entries)
        {
            if (entry.IsHeader) continue;
            if (rule.Within is { } within ? entry is not Bullet item || !within.Any(name => StandsIn(item, name)) : entry is Bullet) continue;
            if (Match(rule.Line, rule.CaseInsensitive, entry) is { } groups)
            {
                yield return new Candidate(rule, null, entry, groups.ToDictionary(g => g.Key, g => g.Value.Value, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>An item nested in an item no rule read, or one that could not be read, is not looked into (FBL 0.7 §4.8).</summary>
    public override bool Admits(IReadOnlyList<string>? within, Entry entry, Func<Entry, string?> claimedBy) =>
        entry.Parent is null || claimedBy(entry.Parent) is not null;

    /// <summary>An item's text is written as it is; only a placeholder of a word slot is quoted, so it stays one word.</summary>
    protected override bool QuotesSpaces(Rule rule, string name) => rule.Attribute(name)?.Word is not null;

    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        if (slot.Kind is not SlotKind.Continuation) return base.Read(candidate, slot);
        if (candidate.Entry is not Bullet item) return SlotRead.ReadOnlyAbsent("A statement has no continuation.");
        return new SlotRead(item.ContinuationText(Text), item.ContinuationSpan(Text), true, true) { Node = item };
    }

    public override void AfterRead(Func<Entry, string?> claimedBy)
    {
        if (!Binding.ReportUnmatched) return;
        foreach (var statement in _entries)
        {
            if (statement is Bullet || statement.IsHeader || claimedBy(statement) is not null) continue;
            Report(FindingCodes.UnboundStatement, FindingSeverity.Warning, "No rule of the binding reads this statement; it is kept as it is.", statement.FirstLineSpan);
        }
    }

    // ---- writing the continuation (FBL 0.7 §6.3) ----

    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Binding.Kind is not SlotKind.Continuation) return base.RemoveSlot(plan, element, change);
        if (change.Read.Span is { } span) plan.Add(SpliceOperation.RemoveKey, span, "");
        return true;
    }

    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        if (change.Binding.Kind is not SlotKind.Continuation) return base.ReplaceSlot(plan, change);
        if (change.Read.Span is not { } span) return false;
        var value = NewText.Plain(change.Value, null);
        if (value == (string?)change.Read.Value) return true;
        if (value.Length == 0) plan.Add(SpliceOperation.RemoveKey, span, "");
        else plan.Add(SpliceOperation.ReplaceValue, span, Columns.Indented(value, ((Bullet)change.Read.Node!).ContentColumn, NewlineAt(span.Start)));
        return true;
    }

    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Binding.Kind is not SlotKind.Continuation) return base.InsertSlot(plan, element, change);
        var value = NewText.Plain(change.Value, null);
        if (value.Length == 0) return true;
        var item = (Bullet)element.Entry;
        var offset = Text.Lines[item.FirstLine].ContentEnd;
        plan.Add(SpliceOperation.InsertKey, offset, offset, Columns.Indented(value, item.ContentColumn, NewlineAt(offset)));
        return true;
    }

    // ---- new and moved items (FBL 0.7 §6.2, §6.3) ----

    public override void Insert(Plan plan, InsertRequest request)
    {
        var rule = request.Rule;
        if (rule.Insert?.Emit is not { } emit)
        {
            Plan.Refuse($"The binding gives no form to write a new {rule.Type} in.");
            return;
        }
        if (rule.Insert.Place is not (InsertPlace.AfterLast or InsertPlace.End or InsertPlace.LastChild or InsertPlace.Start))
        {
            Plan.Refuse($"A bullets body cannot place a new entry '{WireName.Of(rule.Insert.Place)}'.");
        }
        var line = Render(emit, rule, request.Values, request.Id, request.Source?.Key, request.Target?.Key);
        var parent = (Bullet?)request.Parent?.Entry;
        var siblings = Siblings(plan.Reading, rule, request.Parent);
        var index = request.Index ?? (rule.Insert.Place == InsertPlace.Start ? 0 : siblings.Count);
        if (index < 0 || index > siblings.Count) index = siblings.Count;
        var place = Place(plan, rule, parent, siblings, index);
        if (place is null) return;
        var column = Columns.Of(place.Value.Indent) + 2;
        var lines = new StringBuilder(place.Value.Indent).Append(place.Value.Marker).Append(' ').Append(line);
        if (request.Values.TryGetValue(ContinuationAttribute(rule) ?? "", out var notes) && NewText.Plain(notes, null) is { Length: > 0 } continuation)
        {
            lines.Append(Columns.Indented(continuation, column, "\n"));
        }
        var newline = NewlineAt(place.Value.Offset);
        plan.Add(SpliceOperation.InsertEntry, place.Value.Offset, place.Value.Offset, AsLines(lines.ToString(), place.Value.Offset, place.Value.BlankBefore, newline));
    }

    /// <summary>The name of the rule's attribute bound to the continuation, if any.</summary>
    private static string? ContinuationAttribute(Rule rule) => rule.Attributes.FirstOrDefault(a => a.Value.Kind is SlotKind.Continuation).Key;

    /// <summary>
    /// The text of new lines at <paramref name="offset"/>, written with <paramref name="newline"/>: each line
    /// followed by a line ending, or, at the end of a body whose last line has none, each preceded by one and
    /// the body still without a final newline. <paramref name="blankBefore"/> writes one more ending first, so a
    /// blank line stands before the text.
    /// </summary>
    private string AsLines(string text, int offset, bool blankBefore, string newline)
    {
        var lines = text.Split('\n');
        var endless = offset == Text.Length && Text.Length > 0 && Text.Lines[^1].Ending.Length == 0;
        var builder = new StringBuilder();
        if (endless) builder.Append(newline);
        if (blankBefore) builder.Append(newline);
        for (var i = 0; i < lines.Length; i++)
        {
            if (endless && i > 0) builder.Append(newline);
            builder.Append(lines[i]);
            if (!endless) builder.Append(newline);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Where a new item goes among <paramref name="siblings"/> at <paramref name="index"/>, and the indentation and marker
    /// it takes (FBL 0.7 §6.3): after its previous sibling's line span with that sibling's; else at the start of its next
    /// sibling's with that one's; else as its parent's first nested item, with the parent's marker at its content column;
    /// else as the first item of its section, creating the section when it is missing.
    /// </summary>
    private (int Offset, string Indent, char Marker, bool BlankBefore)? Place(Plan plan, Rule rule, Bullet? parent, List<ReadElement> siblings, int index)
    {
        if (index > 0)
        {
            var previous = (Bullet)siblings[index - 1].Entry;
            return (previous.LineSpan!.Value.End, previous.IndentText, previous.Marker, false);
        }
        if (siblings.Count > 0)
        {
            var next = (Bullet)siblings[0].Entry;
            return (next.LineSpan!.Value.Start, next.IndentText, next.Marker, false);
        }
        if (parent is not null)
        {
            var last = parent.Continuation.Count > 0 ? parent.Continuation[^1] : parent.FirstLine;
            return (Text.Lines[last].End, new string(' ', parent.ContentColumn), parent.Marker, false);
        }
        var name = rule.Insert!.Container ?? rule.Within?.FirstOrDefault();
        if (name is not null && _sections.TryGetValue(name, out var section))
        {
            var lastLine = section.First;
            for (var line = section.First + 1; line < section.End; line++)
            {
                if (!IsBlank(line)) lastLine = line;
            }
            return (Text.Lines[lastLine].End, "", '-', true);
        }
        if (rule.Insert.Create is not { At: ContainerPlace.EndOfDocument, Text: { } heading })
        {
            Plan.Refuse($"The file has no section to add the {rule.Type} to.");
            return null;
        }
        var offset = Text.Length;
        var newline = NewlineAt(offset);
        var blankOrEmpty = Text.Length == Text.BomLength || IsBlank(Text.Lines.Count - 1);
        plan.Add(SpliceOperation.EnsureContainer, offset, offset, AsLines(heading, offset, !blankOrEmpty, newline));
        return (offset, "", '-', true);
    }

    private bool IsBlank(int line)
    {
        var span = Text.Lines[line];
        for (var i = span.Start; i < span.ContentEnd; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t')) return false;
        }
        return true;
    }

    /// <summary>
    /// The elements a new or moved item of <paramref name="rule"/> stands among (FBL 0.7 §6.2): those read from items
    /// nested in <paramref name="parent"/>, or, at the top level, the top-level items of the rule's section.
    /// </summary>
    private List<ReadElement> Siblings(BodyReading reading, Rule rule, ReadElement? parent)
    {
        var name = rule.Insert?.Container ?? rule.Within?.FirstOrDefault();
        return reading.Elements
            .Where(element => element.Entry is Bullet item && element.Parent == parent
                && (parent is not null || (item.Parent is null && (name is null || StandsIn(item, name)))))
            .ToList();
    }

    /// <summary>
    /// A move (FBL 0.7 §5.5, §6.3): the item's line span is removed, and inserted where a new item would go at
    /// <paramref name="index"/> among <paramref name="parent"/>'s children, every line that is not blank shifted by the
    /// difference between the column a new item's marker would have there and the column its marker had.
    /// </summary>
    public override void MoveTo(Plan plan, ReadElement element, ReadElement? parent, int index)
    {
        var item = (Bullet)element.Entry;
        var span = item.LineSpan!.Value;
        var siblings = Siblings(plan.Reading, element.Rule, parent).Where(sibling => sibling != element).ToList();
        if (index < 0 || index > siblings.Count) index = siblings.Count;
        var place = Place(plan, element.Rule, (Bullet?)parent?.Entry, siblings, index);
        if (place is null) return;
        var offset = place.Value.Offset;
        // The place is taken in the body before the edit: just after the item's old parent is where the item starts.
        if (index > 0 && Contains(siblings[index - 1].Entry.LineSpan!.Value, span)) offset = span.Start;
        var shift = Columns.Of(place.Value.Indent) - item.MarkerColumn;
        var moved = new StringBuilder();
        foreach ((string line, string ending) in Columns.Lines(Text.Text(span)))
        {
            moved.Append(line.Trim(' ', '\t').Length == 0 ? line : Columns.Shift(line, shift)).Append(ending);
        }
        plan.Add(SpliceOperation.RemoveEntry, span, "");
        plan.Add(SpliceOperation.InsertEntry, offset, offset, moved.ToString());
    }

    private static bool Contains(Span outer, Span inner) => outer.Start <= inner.Start && inner.End <= outer.End;
}
