using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Lines;

/// <summary>
/// A statement of the <c>lines</c> or <c>blocks</c> family (FBL §4.6, §4.7): one line, or, when it
/// opens a block, the lines up to and including the one holding the matching <c>}</c>.
/// </summary>
internal sealed class Statement : Entry
{
    public required int FirstLine { get; init; }

    /// <summary>The statement's first line from its first non-whitespace byte, as the rules' <c>line</c> expressions see it.</summary>
    public required string Content { get; init; }

    /// <summary>The byte offset of every UTF-16 index of <see cref="Content"/>, and one past its end.</summary>
    public required int[] Offsets { get; init; }

    public bool Opens { get; set; }

    public bool IsHeader { get; set; }

    /// <summary>The span of the first line's content, without trailing whitespace: what <c>re-emit-line</c> replaces.</summary>
    public Span FirstLineSpan { get; set; }
}

/// <summary>A named group of one statement as one rule's <c>line</c> matched it.</summary>
internal sealed record Group(string Value, Span Span);

/// <summary>The <c>lines</c> and <c>blocks</c> families (FBL §4.6, §4.7).</summary>
internal sealed class LinesFamily(BodyText text, FblBinding binding, FblOptions options, bool blocks) : FamilyReader(text, binding, options)
{
    private readonly List<Entry> _entries = [];
    private readonly List<Span> _leaves = [];
    private readonly List<Statement> _statements = [];
    private readonly Dictionary<(string Expression, Statement Statement), IReadOnlyDictionary<string, Group>?> _matches = [];
    private readonly HashSet<int> _commentLines = [];

    public override string FamilyName => blocks ? "blocks" : "lines";

    public override IReadOnlyList<Entry> Entries => _entries;

    protected override IReadOnlyList<Span> Leaves => _leaves;

    /// <summary>Between statements and comments there are only whitespace, line endings and the closing <c>}</c> of blocks.</summary>
    protected override bool IsTrivia(Span gap)
    {
        for (var i = gap.Start; i < gap.End; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'}')) return false;
        }
        return true;
    }

    public override void Parse()
    {
        BoundedRegex? comment = Binding.Comment is null ? null : Regex(Binding.Comment, false);
        var lines = Text.Lines;
        var stack = new Stack<Statement>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var start = index == 0 ? Math.Max(line.Start, Text.BomLength) : line.Start;
            var first = start;
            while (first < line.ContentEnd && Text.Bytes[first] is (byte)' ' or (byte)'\t') first++;
            if (first == line.ContentEnd) continue;
            var content = Text.Text(first, line.ContentEnd);
            if (comment is not null && Matches(comment, Text.Text(start, line.ContentEnd), null))
            {
                _commentLines.Add(index);
                _leaves.Add(new Span(first, line.ContentEnd));
                continue;
            }
            var trimmed = content.TrimEnd(' ', '\t');
            if (blocks && trimmed == "}")
            {
                if (stack.Count == 0)
                {
                    Unreadable = (first, "The braces of the body do not balance: this '}' closes no block.");
                    return;
                }
                var opener = stack.Pop();
                opener.LineSpan = new Span(opener.LineSpan!.Value.Start, line.End);
                opener.Own = opener.Own with { End = first + 1 };
                continue;
            }
            var statement = new Statement
            {
                FirstLine = index,
                Content = content,
                Offsets = ByteOffsets(content, first),
                Own = new Span(first, first + Encoding.UTF8.GetByteCount(trimmed)),
                Indent = first - line.Start,
                Parent = stack.Count > 0 ? stack.Peek() : null,
            };
            statement.FirstLineSpan = statement.Own;
            statement.LineSpan = new Span(LeadingCommentStart(index), line.End);
            statement.Parent?.Children.Add(statement);
            _statements.Add(statement);
            _entries.Add(statement);
            _leaves.Add(new Span(first, line.ContentEnd));
            if (blocks)
            {
                (int net, bool endsWithOpen) = Braces(content);
                if (endsWithOpen && net == 1)
                {
                    statement.Opens = true;
                    stack.Push(statement);
                }
                else if (net != 0)
                {
                    Unreadable = (first, "The braces of the body do not balance on this line.");
                    return;
                }
            }
        }
        if (stack.Count > 0)
        {
            Unreadable = (stack.Peek().Own.Start, "The braces of the body do not balance: this block is never closed.");
        }
    }

    private int LeadingCommentStart(int index)
    {
        var start = Text.Lines[index].Start;
        for (var above = index - 1; above >= 0 && _commentLines.Contains(above); above--)
        {
            start = Text.Lines[above].Start;
        }
        return start;
    }

    private static (int Net, bool EndsWithOpen) Braces(string content)
    {
        var net = 0;
        var inString = false;
        var last = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < content.Length) i++;
                else if (c == '"') inString = false;
                last = c;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') net++;
            else if (c == '}') net--;
            if (c is not (' ' or '\t')) last = c;
        }
        return (net, !inString && last == '{');
    }

    private static int[] ByteOffsets(string content, int start)
    {
        var offsets = new int[content.Length + 1];
        var offset = start;
        for (var i = 0; i < content.Length; i++)
        {
            offsets[i] = offset;
            if (char.IsHighSurrogate(content[i]) && i + 1 < content.Length)
            {
                offsets[i + 1] = offset;
                offset += 4;
                i++;
                continue;
            }
            offset += Encoding.UTF8.GetByteCount(content.AsSpan(i, 1));
        }
        offsets[content.Length] = offset;
        return offsets;
    }

    private bool Matches(BoundedRegex regex, string input, Statement? statement)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            if (statement is not null) TimedOut(regex, statement);
            return false;
        }
    }

    private void TimedOut(BoundedRegex regex, Statement statement) =>
        Report(FindingCodes.RegexTimeout, FindingSeverity.Warning, $"The expression '{regex.Expression}' took too long on this statement; it is read as not matching.", statement.Own);

    private IReadOnlyDictionary<string, Group>? Match(string expression, bool caseInsensitive, Statement statement)
    {
        if (_matches.TryGetValue((expression, statement), out var cached)) return cached;
        var regex = Regex(expression, caseInsensitive);
        Dictionary<string, Group>? groups = null;
        try
        {
            if (regex.Match(statement.Content) is { } match)
            {
                groups = new Dictionary<string, Group>(StringComparer.Ordinal);
                foreach (var name in regex.Regex.GetGroupNames())
                {
                    if (int.TryParse(name, out _)) continue;
                    var group = match.Groups[name];
                    if (!group.Success) continue;
                    groups[name] = new Group(group.Value, new Span(statement.Offsets[group.Index], statement.Offsets[group.Index + group.Length]));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            TimedOut(regex, statement);
        }
        _matches[(expression, statement)] = groups;
        return groups;
    }

    public override bool HeaderHolds(HeaderSettings header)
    {
        if (header.Line is null) return true;
        var first = _statements.FirstOrDefault();
        if (first is null) return false;
        if (Match(header.Line, false, first) is null) return false;
        first.IsHeader = true;
        return true;
    }

    public override IEnumerable<Candidate> Candidates(Rule rule)
    {
        if (rule.Line is null) yield break;
        foreach (var statement in _statements)
        {
            if (statement.IsHeader) continue;
            if (Match(rule.Line, rule.CaseInsensitive, statement) is { } groups)
            {
                yield return new Candidate(rule, null, statement, groups.ToDictionary(g => g.Key, g => g.Value.Value, StringComparer.Ordinal));
            }
        }
    }

    public override IEnumerable<Candidate> BlockCandidates(BlockRule block)
    {
        foreach (var statement in _statements)
        {
            if (statement.IsHeader) continue;
            if (Match(block.Line, block.CaseInsensitive, statement) is { } groups)
            {
                yield return new Candidate(null, block, statement, groups.ToDictionary(g => g.Key, g => g.Value.Value, StringComparer.Ordinal));
            }
        }
    }

    public override bool Admits(IReadOnlyList<string>? within, Entry entry, Func<Entry, string?> claimedBy)
    {
        if (!blocks || within is null) return true;
        var enclosing = entry.Parent;
        var name = enclosing is null ? "^" : claimedBy(enclosing);
        return name is not null && within.Contains(name);
    }

    private IReadOnlyDictionary<string, Group> Groups(Candidate candidate)
    {
        var expression = candidate.Rule?.Line ?? candidate.Block!.Line;
        var insensitive = candidate.Rule?.CaseInsensitive ?? candidate.Block!.CaseInsensitive;
        return Match(expression, insensitive, (Statement)candidate.Entry) ?? new Dictionary<string, Group>();
    }

    public override object CelValue(Candidate candidate)
    {
        var map = new CelMap();
        foreach ((string name, string value) in candidate.Captures) map[name] = value;
        return map;
    }

    public override (string Name, object? Value) CelExtra(Candidate candidate) => ("groups", CelValue(candidate));

    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        if (slot.Group is null) return SlotRead.ReadOnlyAbsent($"A {FamilyName} statement has no {slot}.");
        var groups = Groups(candidate);
        if (!groups.TryGetValue(slot.Group, out var group))
        {
            return slot.Flag ? new SlotRead(false, null, false, true) : SlotRead.Absent;
        }
        if (slot.Word is null)
        {
            return new SlotRead(group.Value, group.Span, true, true) { Node = group, Words = Words(group), Wire = group.Value };
        }
        var expression = Regex(slot.Word, false);
        foreach (var word in Words(group))
        {
            var raw = Text.Text(word.Span);
            Match? match;
            try
            {
                match = expression.Match(raw);
            }
            catch (RegexMatchTimeoutException)
            {
                TimedOut(expression, (Statement)candidate.Entry);
                continue;
            }
            if (match is null) continue;
            if (slot.Flag) return new SlotRead(true, word.Span, true, true) { Node = word, Wire = raw };
            var value = match.Groups["value"];
            if (value.Success)
            {
                var offset = word.Span.Start + Encoding.UTF8.GetByteCount(raw.AsSpan(0, value.Index));
                var span = new Span(offset, offset + Encoding.UTF8.GetByteCount(value.Value));
                return new SlotRead(value.Value, span, true, true) { Node = word, Wire = value.Value };
            }
            return new SlotRead(word.Text, word.Span, true, true) { Node = word, Quote = word.Quoted ? "\"" : null, Wire = word.Text };
        }
        return slot.Flag ? new SlotRead(false, null, false, true) : SlotRead.Absent;
    }

    /// <summary>
    /// The words of a group (FBL §4.6): runs of non-whitespace, where a run starting with <c>"</c>
    /// extends to the next <c>"</c> and includes both quotes.
    /// </summary>
    private List<Word> Words(Group group)
    {
        var words = new List<Word>();
        var bytes = Text.Bytes;
        var i = group.Span.Start;
        var end = group.Span.End;
        while (i < end)
        {
            while (i < end && bytes[i] is (byte)' ' or (byte)'\t') i++;
            if (i >= end) break;
            var start = i;
            if (bytes[i] == (byte)'"')
            {
                i++;
                while (i < end && bytes[i] != (byte)'"') i++;
                if (i < end) i++;
                words.Add(new Word(Text.Text(start + 1, Math.Max(start + 1, i - 1)), new Span(start, i), true));
                continue;
            }
            while (i < end && bytes[i] is not ((byte)' ' or (byte)'\t')) i++;
            words.Add(new Word(Text.Text(start, i), new Span(start, i), false));
        }
        return words;
    }

    public override SlotRead ReadRaw(Entry entry, string name)
    {
        foreach (((_, Statement statement), IReadOnlyDictionary<string, Group>? groups) in _matches)
        {
            if (statement == entry && groups is not null && groups.TryGetValue(name, out var group))
            {
                return new SlotRead(group.Value, group.Span, true, true) { Node = group };
            }
        }
        return SlotRead.Absent;
    }

    public override void AfterRead(Func<Entry, string?> claimedBy)
    {
        if (!Binding.ReportUnmatched) return;
        foreach (var statement in _statements)
        {
            if (statement.IsHeader || claimedBy(statement) is not null) continue;
            Report(FindingCodes.UnboundStatement, FindingSeverity.Warning, "No rule of the binding reads this statement; it is kept as it is.", statement.FirstLineSpan);
        }
    }

    // ---- writing ----

    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        if (Words(value) is { } words) return string.Join(" ", words.Select(word => Format(read with { Quote = null, Wire = null }, binding, word)));
        var written = binding is null ? null : NewText.Wire(binding, value, read.Wire);
        written ??= NewText.Plain(value, binding);
        if (read.Quote is { } quote) return quote + written + quote;
        return read.Node is Word && HasSpace(written) ? "\"" + written + "\"" : written;
    }

    /// <summary>The written form of a value for an emit placeholder: a map's first key, a flag's word, a number by FBL §6.3.</summary>
    private string Emitted(AttributeBinding? binding, object? value)
    {
        if (Words(value) is { } words) return string.Join(" ", words.Select(word => Emitted(binding, word)));
        if (binding is null) return NewText.Plain(value, null);
        if (binding.Flag) return value is true ? FlagWord(binding) : "";
        return NewText.Wire(binding, value, null) ?? NewText.Plain(value, binding);
    }

    /// <summary>Whether a value written as one word would read back as several (FBL §4.6), so it is written between quotes.</summary>
    private static bool HasSpace(string text) => text.Any(char.IsWhiteSpace);

    /// <summary>
    /// <paramref name="text"/> as placeholder <paramref name="name"/> of <paramref name="template"/> writes it:
    /// between quotes where it holds a space and the template does not quote it itself.
    /// </summary>
    private static string? AsWord(string template, string name, object? value, string? text) =>
        text is not null && Words(value) is null && HasSpace(text) && !template.Contains("\"{" + name + "}\"", StringComparison.Ordinal)
            ? "\"" + text + "\""
            : text;

    /// <summary>Whether <paramref name="value"/> is the number <paramref name="read"/> already holds, however it is spelled there.</summary>
    private static bool SameNumber(SlotRead read, object? value) =>
        NewText.TryNumber(value, out var number) && read.Wire is { } wire
        && double.TryParse(wire, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var held)
        && held.Equals(number);

    /// <summary>Whether <paramref name="value"/> is the list of words <paramref name="read"/> already holds, however they are spaced there.</summary>
    private static bool SameWords(SlotRead read, object? value) =>
        Words(value) is { } items && read.Words is { } words
        && items.Select(item => NewText.Plain(item, null)).SequenceEqual(words.Select(word => word.Text), StringComparer.Ordinal);

    /// <summary>The items of a list value, which a group holds as its words (FBL §4.6); null for any other value.</summary>
    private static IEnumerable<object?>? Words(object? value) => value is not string and IEnumerable<object?> list ? list : null;

    private static string FlagWord(Slot slot)
    {
        var word = slot.Word ?? "";
        if (word.StartsWith('^')) word = word[1..];
        if (word.EndsWith('$')) word = word[..^1];
        return System.Text.RegularExpressions.Regex.Unescape(word);
    }

    /// <summary>A flag is never removed as an emptied value: it is written as present or not (<see cref="ReplaceSlot"/>, <see cref="InsertSlot"/>).</summary>
    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Binding.Flag) return false;
        if (change.Read.Present) plan.Add(SpliceOperation.RemoveKey, Removable(change.Read), "");
        return true;
    }

    protected override bool ReplaceSlot(Plan plan, ReadElement element, SlotChange change)
    {
        var read = change.Read;
        if (change.Binding.Flag)
        {
            if (change.Value is not true) plan.Add(SpliceOperation.RemoveKey, WithSpaceBefore(read.Span!.Value), "");
            return true;
        }
        if (read.Span is not { } readSpan) return false;
        if (SameNumber(read, change.Value) || SameWords(read, change.Value)) return true;
        var span = AfterLeadingSpace(read, readSpan);
        var text = Format(read, change.Binding, change.Value);
        if (text != Text.Text(span)) plan.Add(SpliceOperation.ReplaceValue, span, text);
        return true;
    }

    /// <summary>False when the line has no place for the value, and it is re-emitted (<see cref="Rewrite"/>).</summary>
    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Binding.Flag && change.Value is not true) return true;
        if (InsertPoint(element, change) is not { } point) return false;
        plan.Add(SpliceOperation.InsertKey, point.Offset, point.Offset, point.Text);
        return true;
    }

    protected override bool ReadsBack(IReadOnlyList<Splice> planned, ReadElement element, IReadOnlyList<SlotChange> changes) =>
        RemovalsReadBack(element, (Statement)element.Entry, changes, planned);

    /// <summary>The line re-emitted through the rule's <c>emit</c> with the changes, or refused when it has none.</summary>
    protected override void Rewrite(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes)
    {
        var statement = (Statement)element.Entry;
        if (element.Rule.Insert?.Emit is not { } emit)
        {
            Plan.Refuse($"This {element.Rule.Type} has no place in its line for the new value, and its rule gives no form to write the line in.");
            return;
        }
        var values = new Dictionary<string, object?>(element.Attributes, StringComparer.Ordinal);
        foreach (var change in changes) values[change.Attribute] = change.IsEmpty ? null : change.Value;
        var line = NewText.Render(emit, name => AsWord(emit, name, values.GetValueOrDefault(name), Placeholder(element.Rule, name, values, element.IdRead?.Value as string ?? element.Id,
            element.SourceRead?.Value as string, element.TargetRead?.Value as string)));
        plan.Add(SpliceOperation.ReEmitLine, ReEmitSpan(statement), line);
    }

    /// <summary>
    /// Whether the line, with <paramref name="planned"/> applied, still reads as the change says where a
    /// value is removed: the rule still matches it, every group the change does not write reads as it
    /// did, and a value that has a group of its own no longer reads one. A removal can fail that - the word after the value is glued to the
    /// word before it, or moves into the value's place - and the line is then re-emitted instead.
    /// </summary>
    private bool RemovalsReadBack(ReadElement element, Statement statement, IReadOnlyList<SlotChange> changes, IReadOnlyList<Splice> planned)
    {
        if (planned.All(splice => splice.Operation != SpliceOperation.RemoveKey) || element.Rule.Line is not { } expression) return true;
        var start = statement.Offsets[0];
        var end = statement.Offsets[^1];
        var after = new StringBuilder();
        var at = start;
        foreach (var splice in planned.OrderBy(splice => splice.Start))
        {
            if (splice.Start < at || splice.End > end) return true;
            after.Append(Text.Text(at, splice.Start)).Append(splice.Text);
            at = splice.End;
        }
        after.Append(Text.Text(at, end));
        Match? match;
        try
        {
            match = Regex(expression, element.Rule.CaseInsensitive).Match(after.ToString());
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        if (match is null) return false;
        var changed = changes.Select(change => change.Binding.Group).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach ((string name, Group held) in Groups(statement, expression, element.Rule.CaseInsensitive))
        {
            if (!changed.Contains(name) && match.Groups[name].Value != held.Value) return false;
        }
        foreach (var change in changes)
        {
            if (!change.IsEmpty || change.Binding.Group is not { } group) continue;
            var own = element.Rule.Attributes.Count(attribute => attribute.Value.Group == group) == 1;
            if (own && match.Groups[group].Success) return false;
        }
        return true;
    }

    private IReadOnlyDictionary<string, Group> Groups(Statement statement, string expression, bool caseInsensitive) =>
        Match(expression, caseInsensitive, statement) ?? new Dictionary<string, Group>();

    private Span ReEmitSpan(Statement statement)
    {
        if (!statement.Opens) return statement.FirstLineSpan;
        var span = statement.FirstLineSpan;
        var end = span.End - 1;
        while (end > span.Start && Text.Bytes[end - 1] is (byte)' ' or (byte)'\t') end--;
        return span with { End = end };
    }

    private string? Placeholder(Rule rule, string name, IReadOnlyDictionary<string, object?> values, string? id, string? source, string? target)
    {
        switch (name)
        {
            case "id": return id;
            case "source": return source;
            case "target": return target;
        }
        var binding = rule.Attribute(name);
        return values.TryGetValue(name, out var value) && value is not null ? Emitted(binding, value) : null;
    }

    private Span WithSpaceBefore(Span span)
    {
        var start = span.Start;
        while (start > 0 && Text.Bytes[start - 1] is (byte)' ' or (byte)'\t') start--;
        return span with { Start = start };
    }

    /// <summary>
    /// The span a new value of a whole group replaces: the group without the whitespace it opens with,
    /// which separates it from what comes before and is the line's, not the value's.
    /// </summary>
    private Span AfterLeadingSpace(SlotRead read, Span span)
    {
        if (read.Node is not Group) return span;
        var start = span.Start;
        while (start < span.End && Text.Bytes[start] is (byte)' ' or (byte)'\t') start++;
        return span with { Start = start };
    }

    /// <summary>
    /// A removed value takes its quotes and the whitespace before it with it (FBL §6.1 <c>remove-key</c>),
    /// and the whole word when it was read from part of one, as <c>size=(?&lt;value&gt;.+)</c> reads it.
    /// </summary>
    private Span Removable(SlotRead read)
    {
        var span = read.Node is Word word ? word.Span : read.Span!.Value;
        if (span.Start > 0 && span.End < Text.Length && Text.Bytes[span.Start - 1] == (byte)'"' && Text.Bytes[span.End] == (byte)'"')
        {
            span = new Span(span.Start - 1, span.End + 1);
        }
        return WithSpaceBefore(span);
    }

    /// <summary>
    /// Where an absent value can be written by <c>insert-key</c> (FBL §6.3), which a host must prefer to
    /// re-emitting the line because it keeps every word no rule binds: when the rule's emit places it
    /// right after a value the statement has, and every value the emit places after it stands after
    /// that point, so nothing must move. Null when the line must be re-emitted instead.
    /// </summary>
    private (int Offset, string Text)? InsertPoint(ReadElement element, SlotChange change)
    {
        if (element.Rule.Insert?.Emit is not { } emit) return null;
        var parts = NewText.Parts(emit).ToList();
        var index = parts.FindIndex(p => p.Placeholder == change.Attribute);
        if (index < 0 || parts[index].Segment < 0) return null;
        var segment = parts[index].Segment;
        var first = parts.FindIndex(p => p.Segment == segment);
        var previous = -1;
        for (var i = first - 1; i >= 0; i--)
        {
            if (parts[i].Placeholder is not null) { previous = i; break; }
        }
        if (previous < 0) return null;
        if (SpanOf(element, parts[previous].Placeholder!) is not { } after) return null;
        for (var i = index + 1; i < parts.Count; i++)
        {
            if (parts[i].Placeholder is { } later && SpanOf(element, later) is { } span && span.Start < after.End) return null;
        }
        var offset = after.End;
        // What the emit writes between that value and the new one, outside the optional parts left out.
        var own = parts[previous].Segment;
        var between = string.Concat(parts.Skip(previous + 1).Take(first - previous - 1).Where(p => p.Segment < 0 || p.Segment == own).Select(p => p.Literal ?? ""));
        if (between.Length > 0 && offset + Encoding.UTF8.GetByteCount(between) <= Text.Length && Text.Text(offset, offset + Encoding.UTF8.GetByteCount(between)) == between)
        {
            offset += Encoding.UTF8.GetByteCount(between);
        }
        var template = "[" + string.Concat(parts.Where(p => p.Segment == segment).Select(p => p.Literal ?? "{" + p.Placeholder + "}")) + "]";
        var segmentText = NewText.Render(template, name => name == change.Attribute ? AsWord(template, name, change.Value, Emitted(change.Binding, change.Value)) : null);
        return segmentText.Length == 0 ? null : (offset, segmentText);
    }

    private Span? SpanOf(ReadElement element, string placeholder) => placeholder switch
    {
        "id" => element.IdRead?.Span,
        "source" => element.SourceRead?.Span,
        "target" => element.TargetRead?.Span,
        _ => element.Slots.TryGetValue(placeholder, out var read) && read.Present ? read.Span : null,
    };

    public override void Insert(Plan plan, InsertRequest request)
    {
        var rule = request.Rule;
        if (rule.Insert?.Emit is not { } emit)
        {
            Plan.Refuse($"The binding gives no form to write a new {rule.Type} in.");
            return;
        }
        var line = NewText.Render(emit, name => AsWord(emit, name, request.Values.GetValueOrDefault(name), Placeholder(rule, name, request.Values, request.Id, request.Source?.Key, request.Target?.Key)));
        var insert = rule.Insert;
        Statement? container = null;
        if (blocks)
        {
            if (request.Parent is { } parent) container = (Statement)parent.Entry;
            else if (insert.Container is { } name) container = FindContainer(plan.Reading, name);
            if (container is null && (request.Parent is not null || insert.Container is not null))
            {
                Plan.Refuse($"The file has no block to add the {rule.Type} to.");
            }
        }
        var siblings = container is null ? _statements.Where(s => s.Parent is null && !s.IsHeader).ToList() : container.Children.OfType<Statement>().ToList();
        Statement? previous = null;
        switch (insert.Place)
        {
            case "after-last":
                previous = siblings.LastOrDefault(s => plan.Reading.ClaimedBy(s) == rule.Name) ?? siblings.LastOrDefault();
                break;
            case "end":
            case "last-child":
                previous = siblings.LastOrDefault();
                break;
            case "start":
                previous = null;
                break;
            case "end-of-document":
                previous = _statements.LastOrDefault();
                break;
            default:
                Plan.Refuse($"A {FamilyName} body cannot place a new entry '{insert.Place}'.");
                break;
        }
        if (container is not null && !container.Opens)
        {
            OpenBlock(plan, container, line);
            return;
        }
        int offset;
        if (previous is not null) offset = previous.LineSpan!.Value.End;
        else if (container is not null) offset = Text.Lines[container.FirstLine].End;
        else offset = siblings.FirstOrDefault()?.LineSpan?.Start ?? Text.Length;
        var indent = previous?.Indent ?? siblings.FirstOrDefault()?.Indent ?? (container is null ? 0 : container.Indent + Step);
        plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, Indentation(indent) + line));
    }

    /// <summary>The text of a new line at <paramref name="offset"/>, a line start or the end of a body without a final newline (FBL §6.3).</summary>
    private string NewLine(int offset, string line)
    {
        var newline = NewlineAt(offset);
        if (offset == Text.Length && Text.Length > 0 && Text.Lines[^1].Ending.Length == 0) return newline + line;
        return line + newline;
    }

    private void OpenBlock(Plan plan, Statement parent, string line)
    {
        var first = Text.Lines[parent.FirstLine];
        var newline = NewlineAt(first.Start);
        plan.Add(SpliceOperation.OpenBlock, parent.FirstLineSpan.End, parent.FirstLineSpan.End, " {");
        var childIndent = Indentation(parent.Indent + Step);
        var close = Indentation(parent.Indent) + "}";
        if (first.Ending.Length == 0)
        {
            plan.Add(SpliceOperation.InsertEntry, first.End, first.End, newline + childIndent + line);
            plan.Add(SpliceOperation.OpenBlock, first.End, first.End, newline + close);
            return;
        }
        plan.Add(SpliceOperation.InsertEntry, first.End, first.End, childIndent + line + newline);
        plan.Add(SpliceOperation.OpenBlock, first.End, first.End, close + newline);
    }

    private static Statement? FindContainer(BodyReading reading, string name) =>
        reading.Family.Entries.OfType<Statement>().FirstOrDefault(s => reading.ClaimedBy(s) == name);

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed) =>
        plan.Add(SpliceOperation.RemoveEntry, element.Entry.RemovalSpan, "");
}
