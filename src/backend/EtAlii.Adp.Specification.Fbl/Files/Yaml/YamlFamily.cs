using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;
using YamlDotNet.Core;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// The <c>yaml</c> family (FBL §4.3): YamlDotNet decides whether the stream is well-formed, the
/// <see cref="YamlParser"/> reads the first document's structure and spans, and writing keeps each
/// replaced scalar's style and the body's indentation (FBL §6.3).
/// </summary>
internal sealed class YamlFamily(BodyText text, FblBinding binding, FblOptions options) : TreeFamily(text, binding, options)
{
    private List<Span> _leaves = [];

    // The attribute bindings the host says hold a date or a date-time, found once by reference.
    private readonly HashSet<AttributeBinding> _timeTyped = [.. binding.AllRules
        .SelectMany(rule => rule.Attributes.Where(attribute => options.TimeAttributes.Contains($"{rule.Name}.{attribute.Key}")).Select(attribute => attribute.Value))];

    public override string FamilyName => "yaml";

    protected override IReadOnlyList<Span> Leaves => _leaves;

    public override void Parse()
    {
        if (WellFormed() is { } problem)
        {
            Unreadable = problem;
            return;
        }
        var parser = new YamlParser(Text);
        try
        {
            var value = parser.ParseDocument();
            Root = new TreeEntry { IsRoot = true, Own = value.Span, Value = value, Indent = 0 };
            Index(Root);
        }
        catch (YamlParser.YamlError error)
        {
            Unreadable = (error.Offset, error.Message);
            return;
        }
        _leaves = parser.Leaves;
        foreach ((string name, Span span) in parser.Duplicates)
        {
            Report(FindingCodes.DuplicateKey, FindingSeverity.Warning, $"The key '{name}' appears again in this mapping; only the first is read.", span);
        }
        foreach (var entry in AllEntries.OfType<TreeEntry>().Where(e => !e.IsRoot)) entry.LineSpan = LineSpanOf(entry);
    }

    /// <summary>YamlDotNet reads the whole stream; a syntax error anywhere makes the body unreadable (FBL §4.3, §7.5).</summary>
    private (int Offset, string Message)? WellFormed()
    {
        var content = Text.Text(Text.BomLength, Text.Length);
        try
        {
            var parser = new Parser(new StringReader(content));
            while (parser.MoveNext())
            {
            }
            return null;
        }
        catch (YamlException e)
        {
            var index = (int)Math.Clamp(e.Start.Index, 0, content.Length);
            return (Text.BomLength + Encoding.UTF8.GetByteCount(content.AsSpan(0, index)), $"The body is not well-formed YAML: {e.Message}");
        }
    }

    /// <summary>Between leaves there is only whitespace, comments, indicators, anchors, tags and document markers.</summary>
    protected override bool IsTrivia(Span gap)
    {
        var bytes = Text.Bytes;
        for (var i = gap.Start; i < gap.End; i++)
        {
            switch (bytes[i])
            {
                case (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)':' or (byte)'-' or (byte)'?' or (byte)',' or (byte)'.':
                    continue;
                case (byte)'#' or (byte)'%':
                    while (i < gap.End && bytes[i] is not ((byte)'\r' or (byte)'\n')) i++;
                    continue;
                case (byte)'&' or (byte)'!':
                    while (i < gap.End && bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) i++;
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// An entry's line span (FBL §4.1.1) when it starts and ends its lines: from its first line,
    /// extended over the comment lines directly above it at its indentation, to the end of its last
    /// line's ending, a trailing comment included.
    /// </summary>
    private Span? LineSpanOf(TreeEntry entry)
    {
        if (!OnlyWhitespaceBefore(entry.Own.Start)) return null;
        if (!OnlyTriviaAfter(entry.Own.End, (byte)'#')) return null;
        var first = Text.LineIndexAt(entry.Own.Start);
        var last = Text.LineIndexAt(Math.Max(entry.Own.Start, entry.Own.End - 1));
        var start = Text.Lines[first].Start;
        for (var above = first - 1; above >= 0; above--)
        {
            var line = Text.Lines[above];
            var p = line.Start;
            while (p < line.ContentEnd && Text.Bytes[p] == (byte)' ') p++;
            if (p >= line.ContentEnd || Text.Bytes[p] != (byte)'#' || p - line.Start != entry.Indent) break;
            start = line.Start;
        }
        return new Span(start, Text.Lines[last].End);
    }

    private int LineEndAfter(TreeEntry entry) => entry.LineSpan?.End ?? Text.Lines[Text.LineIndexAt(Math.Max(entry.Own.Start, entry.Own.End - 1))].End;

    // ---- new text (FBL §6.3) ----

    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        var old = (read.Node as TreeEntry)?.Value;
        var wire = binding is null ? null : NewText.Wire(binding, value, read.Wire);
        var written = Scalar(wire ?? value, old, binding, (read.Node as TreeEntry)?.Indent ?? 0);
        return old?.Style == ValueStyle.Empty ? " " + written : written;
    }

    private string Scalar(object? value, TreeValue? old, AttributeBinding? binding, int keyIndent)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => String(s, old, binding, keyIndent),
            IEnumerable<object?> list => "[" + string.Join(", ", list.Select(v => v is string item ? FlowItem(item) : Scalar(v, null, binding, keyIndent))) + "]",
            _ => NewText.TryNumber(value, out var number) ? NewText.Number(number, binding?.Decimals) : String(NewText.Plain(value, binding), old, binding, keyIndent),
        };
    }

    private string String(string value, TreeValue? old, AttributeBinding? binding, int keyIndent)
    {
        var timeTyped = binding?.KeepTimePrecision == true || (binding is not null && _timeTyped.Contains(binding));
        if (binding?.KeepTimePrecision == true && old is { Kind: ValueKind.Scalar, Text.Length: > 0 }) value = NewText.KeepPrecision(old.Text, value) ?? value;
        var multiline = value.Contains('\n') || value.Contains('\r');
        switch (old?.Style)
        {
            case ValueStyle.Plain when YamlScalars.IsPlainWritable(value, timeTyped):
                return value;
            case ValueStyle.Single when !multiline:
                return YamlScalars.SingleQuoted(value);
            case ValueStyle.Double:
                return YamlScalars.DoubleQuoted(value);
            case ValueStyle.Literal when multiline:
                return Literal(value, keyIndent);
        }
        switch (binding?.Style)
        {
            case "single" when !multiline: return YamlScalars.SingleQuoted(value);
            case "double": return YamlScalars.DoubleQuoted(value);
            case "literal" when multiline: return Literal(value, keyIndent);
            case "plain" when YamlScalars.IsPlainWritable(value, timeTyped): return value;
        }
        if (YamlScalars.IsPlainSafe(value, timeTyped)) return value;
        return Binding.Text.Quote == "single" && !multiline ? YamlScalars.SingleQuoted(value) : YamlScalars.DoubleQuoted(value);
    }

    /// <summary>
    /// A string inside a flow collection: plain only when it is plain-safe and holds none of the flow
    /// indicators <c>, [ ] { }</c> nor a <c>:</c>, which a flow collection would read as a separator or a
    /// mapping; double-quoted otherwise (FBL §6.3).
    /// </summary>
    private static string FlowItem(string value) =>
        YamlScalars.IsPlainSafe(value, false) && value.IndexOfAny([',', '[', ']', '{', '}', ':']) < 0 ? value : YamlScalars.DoubleQuoted(value);

    /// <summary>A multi-line string written <c>|-</c>, its lines one step deeper than its key (FBL §6.3).</summary>
    private string Literal(string value, int keyIndent)
    {
        var newline = Text.DominantEnding ?? Binding.Text.Newline;
        var indent = Indentation(keyIndent + Step);
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return "|-" + string.Concat(lines.Select(l => newline + (l.Length == 0 ? "" : indent + l)));
    }

    // ---- writing ----

    public override void Write(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes)
    {
        foreach (var change in changes)
        {
            var read = change.Read;
            var member = read.Node as TreeEntry;
            if (change is { IsEmpty: true, Binding.Empty: "remove" })
            {
                if (member is not null) plan.Add(SpliceOperation.RemoveKey, member.LineSpan ?? member.Own, "");
                continue;
            }
            if (read.Present && member is not null)
            {
                var written = Format(read, change.Binding, change.Value);
                if (written != Text.Text(member.Value.Span)) plan.Add(SpliceOperation.ReplaceValue, member.Value.Span, written);
                continue;
            }
            var mapping = read.Node as TreeValue ?? Mapping((TreeEntry)element.Entry, change.Binding.Child);
            if (mapping is null || mapping.Kind != ValueKind.Mapping || change.Binding.Key is not { } key)
            {
                Plan.Refuse($"The {Binding.Name} file has no mapping to write '{change.Binding.Key}' in.");
                return;
            }
            InsertKey(plan, mapping, key, change);
        }
    }

    /// <summary>A key the entry lacks goes at its place in the rule's key order: after the nearest present key before it, else before the nearest after it.</summary>
    private void InsertKey(Plan plan, TreeValue mapping, string key, SlotChange change)
    {
        var order = KeyOrder(change.Rule, change.Binding.Child);
        var index = order.IndexOf(key);
        int offset;
        TreeEntry? before = null;
        for (var i = index - 1; i >= 0 && before is null; i--) before = mapping.Member(order[i]);
        if (before is not null)
        {
            offset = LineEndAfter(before);
        }
        else
        {
            TreeEntry? after = null;
            for (var i = index + 1; i < order.Count && after is null; i++) after = mapping.Member(order[i]);
            offset = after?.LineSpan?.Start ?? LineEndAfter(mapping.Entries[^1]);
        }
        var indent = Indentation(mapping.Entries[0].Indent);
        var line = indent + key + ": " + Scalar(change.Value, null, change.Binding, mapping.Entries[0].Indent);
        plan.Add(SpliceOperation.InsertKey, offset, offset, NewLine(offset, [line]));
    }

    /// <summary>The order keys are written in: the rule's <c>insert.keys</c>, then its bindings' keys in binding order.</summary>
    private static List<string> KeyOrder(Rule rule, string? child)
    {
        var order = new List<string>();
        if (child is null) order.AddRange(rule.Insert?.Keys ?? []);
        void Add(Slot? slot)
        {
            if (slot?.Key is { } key && slot.Child == child && !order.Contains(key)) order.Add(key);
        }
        Add(rule.Id?.From);
        Add(rule.Source);
        Add(rule.Target);
        foreach ((_, AttributeBinding binding) in rule.Attributes) Add(binding);
        return order;
    }

    /// <summary>
    /// The text of new lines inserted at <paramref name="offset"/>, a line start: each line with the
    /// ending of the line the insertion point is on. At the end of a body without a final newline the
    /// break goes before the text and none after it (FBL §6.3).
    /// </summary>
    private string NewLine(int offset, IReadOnlyList<string> lines)
    {
        var newline = offset > 0 ? NewlineAt(offset - 1) : NewlineAt(0);
        if (offset == Text.Length && Text.Length > 0 && Text.Lines[^1].Ending.Length == 0)
        {
            return string.Concat(lines.Select(l => newline + l));
        }
        return string.Concat(lines.Select(l => l + newline));
    }

    /// <summary>The columns between a key and the <c>-</c> of its sequence's items, as the body shows it, else <c>text.sequenceIndent</c>.</summary>
    private int SequenceOffset
    {
        get
        {
            foreach (var entry in AllEntries.OfType<TreeEntry>())
            {
                if (entry.KeySpan is not null && entry.LineSpan is not null && entry.Value is { Kind: ValueKind.Sequence, Entries.Count: > 0 } sequence)
                {
                    return sequence.Entries[0].Indent - entry.Indent;
                }
            }
            return Binding.Text.SequenceFlush ? 0 : Step;
        }
    }

    public override void Insert(Plan plan, InsertRequest request)
    {
        var rule = request.Rule;
        var insert = rule.Insert!;
        // FBL §5's place: {before: key} is not implemented here yet; refused, as the lines family does, rather than placed at the end.
        if (insert.Place == "before") Plan.Refuse($"A {FamilyName} body cannot place a new entry '{insert.Place}'.");
        var parent = request.Parent?.Entry ?? EndParent(request);
        var captures = request.Parent?.Candidate.Captures ?? (parent is null ? null : plan.Reading.ElementOf(parent)?.Candidate.Captures) ?? CapturesOf(plan);
        var selector = insert.Container ?? ContainerOf(rule.At);
        var container = Container(selector, parent, captures);
        var keys = ItemLines(request);
        if (keys.Count == 0)
        {
            Plan.Refuse($"A new {rule.Type} would have no keys to write.");
            return;
        }
        if (container is null)
        {
            EnsureContainer(plan, insert, selector, parent, keys);
            return;
        }
        var value = container.Value;
        if (value.Kind == ValueKind.Sequence)
        {
            var siblings = value.Entries;
            var previous = insert.Place switch
            {
                "start" => null,
                "after-last" => siblings.LastOrDefault(s => plan.Reading.ClaimedBy(s) == rule.Name) ?? siblings.LastOrDefault(),
                _ => siblings.LastOrDefault(),
            };
            var model = previous ?? siblings[0];
            var dash = model.Indent;
            var keyIndent = model.Value is { Kind: ValueKind.Mapping, Entries.Count: > 0 } mapping ? mapping.Entries[0].Indent : dash + 2;
            var offset = previous is null ? siblings[0].LineSpan?.Start ?? siblings[0].Own.Start : LineEndAfter(previous);
            plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, Item(keys, dash, keyIndent)));
            return;
        }
        if (value.Style == ValueStyle.Empty)
        {
            var offset = LineEndAfter(container);
            var dash = container.Indent + SequenceOffset;
            plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, Item(keys, dash, dash + 2)));
            return;
        }
        if (value is { Style: ValueStyle.FlowSequence, Flow: List<object?> { Count: 0 } } && container.KeySpan is { } keySpan)
        {
            // An empty flow sequence ("elements: []", the template's) becomes a block sequence: the
            // flow value goes and the item follows on its own line.
            var colon = Text.Text(keySpan.End, value.Span.Start).IndexOf(':', StringComparison.Ordinal);
            plan.Add(SpliceOperation.ReplaceValue, keySpan.End + colon + 1, value.Span.End, "");
            var offset = LineEndAfter(container);
            var dash = container.Indent + SequenceOffset;
            plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, Item(keys, dash, dash + 2)));
            return;
        }
        Plan.Refuse($"The {selector} of this file is not a list a {rule.Type} can be added to.");
    }

    private static string ContainerOf(string? at)
    {
        if (at is null) return "/";
        var cut = at.LastIndexOf('/');
        return cut <= 0 ? "/" : at[..cut];
    }

    /// <summary>The entry a relation is written inside when one of its ends is its enclosing entry (a Databricks dependency inside its task).</summary>
    private static Entry? EndParent(InsertRequest request)
    {
        if (request.Rule.Target?.Parent is not null) return request.Target?.Entry;
        if (request.Rule.Source?.Parent is not null) return request.Source?.Entry;
        return null;
    }

    private void EnsureContainer(Plan plan, InsertSettings insert, string selector, Entry? parent, List<string> keys)
    {
        if (insert.Create is not { } create)
        {
            Plan.Refuse($"The file has no {selector.TrimStart('/')} to add to.");
            return;
        }
        var name = selector.TrimEnd('/')[(selector.TrimEnd('/').LastIndexOf('/') + 1)..];
        var owner = selector.StartsWith('/') ? Root : parent as TreeEntry;
        if (selector.StartsWith('/') && selector.Trim('/').Contains('/')) owner = Container(ContainerOf(selector), parent, new Dictionary<string, string>());
        var mapping = owner?.Value;
        int offset;
        int indent;
        switch (create.At)
        {
            case "end-of-document":
                offset = Text.Length;
                indent = mapping is { Kind: ValueKind.Mapping, Entries.Count: > 0 } ? mapping.Entries[0].Indent : 0;
                break;
            case "after" when mapping?.Member(create.Argument!) is { } after:
                offset = LineEndAfter(after);
                indent = after.Indent;
                break;
            case "before" when mapping?.Member(create.Argument!) is { } before:
                offset = before.LineSpan?.Start ?? before.Own.Start;
                indent = before.Indent;
                break;
            // Before a key the file does not have: at its end, as after-last falls back to end.
            case "before" when mapping is { Kind: ValueKind.Mapping, Entries.Count: > 0 }:
                offset = Text.Length;
                indent = mapping.Entries[0].Indent;
                break;
            case "under" when Container(create.Argument!, parent, new Dictionary<string, string>()) is { Value: { Kind: ValueKind.Mapping, Entries.Count: > 0 } under }:
                offset = LineEndAfter(under.Entries[^1]);
                indent = under.Entries[0].Indent;
                break;
            default:
                Plan.Refuse($"The file has no place to create {name} in.");
                return;
        }
        var containerText = create.Text ?? Indentation(indent) + name + ":";
        plan.Add(SpliceOperation.EnsureContainer, offset, offset, NewLine(offset, [containerText]));
        var dash = indent + SequenceOffset;
        plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, Item(keys, dash, dash + 2)));
    }

    private List<string> Item(List<string> lines, int dash, int keyIndent)
    {
        var item = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = Nested(lines[i], keyIndent);
            item.Add(i == 0 ? Indentation(dash) + "-" + new string(' ', Math.Max(1, keyIndent - dash - 1)) + line : Indentation(keyIndent) + line);
        }
        return item;
    }

    /// <summary>
    /// A key line whose value runs over several lines (a literal block, written relative to column 0)
    /// with its continuation lines moved under the key's indentation; blank ones stay empty.
    /// </summary>
    private string Nested(string line, int keyIndent)
    {
        if (!line.Contains('\n', StringComparison.Ordinal)) return line;
        var parts = line.Split('\n');
        for (var j = 1; j < parts.Length; j++)
        {
            if (parts[j].TrimEnd('\r').Length > 0) parts[j] = Indentation(keyIndent) + parts[j];
        }
        return string.Join('\n', parts);
    }

    /// <summary>The key lines of a new item in <c>insert.keys</c> order, then the skeleton's lines.</summary>
    private List<string> ItemLines(InsertRequest request)
    {
        var rule = request.Rule;
        var lines = new List<string>();
        foreach (var key in KeyOrder(rule, null))
        {
            if (rule.Insert!.Keys.Count > 0 && !rule.Insert.Keys.Contains(key)) continue;
            if (WireValue(request, key) is { } value) lines.Add($"{key}: {value}");
        }
        if (rule.Insert!.Skeleton is { } skeleton)
        {
            var rendered = NewText.Render(skeleton, name => request.Values.TryGetValue(name, out var v) && v is not null ? NewText.Plain(v, rule.Attribute(name)) : name == "id" ? request.Id : null);
            lines.AddRange(rendered.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n'));
        }
        return lines;
    }

    private string? WireValue(InsertRequest request, string key)
    {
        var rule = request.Rule;
        if (rule.Id?.From?.Key == key && request.Id is { } id) return Scalar(id, null, null, 0);
        if (rule.Source?.Key == key && request.Source is { } source) return Scalar(source.Key, null, null, 0);
        if (rule.Target?.Key == key && request.Target is { } target) return Scalar(target.Key, null, null, 0);
        foreach ((string name, AttributeBinding binding) in rule.Attributes)
        {
            if (binding.Key != key || binding.Child is not null || binding.IsComputed) continue;
            if (!request.Values.TryGetValue(name, out var value) || (NewText.IsEmpty(value) && binding.Empty != "keep")) continue;
            var wire = NewText.Wire(binding, value, null);
            return Scalar(wire ?? value, null, binding, 0);
        }
        return null;
    }

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed)
    {
        var entry = (TreeEntry)element.Entry;
        if (entry.IsRoot)
        {
            Plan.Refuse($"The whole {Binding.Name} file cannot be removed.");
            return;
        }
        var span = entry.LineSpan ?? entry.Own;
        if (element.Rule.Remove?.RemoveContainerWhenEmpty == true && entry.Parent is TreeEntry { IsRoot: false, LineSpan: { } containerLine } container)
        {
            var gone = removed.Select(r => r.Entry).ToHashSet();
            if (container.Value.Entries.All(gone.Contains))
            {
                var first = container.Value.Entries[0];
                var firstStart = (first.LineSpan ?? first.Own).Start;
                var head = containerLine with { End = firstStart };
                if (!plan.Touches(head)) plan.Add(SpliceOperation.RemoveContainer, head, "");
            }
        }
        plan.Add(SpliceOperation.RemoveEntry, WithoutFinalNewline(span, removed), "");
    }

    /// <summary>
    /// The last entry of a body without a final newline, removed with the line ending before it, so the
    /// body still has no final newline - as an insertion after such a line keeps it so (FBL §6.3). Not
    /// when the entry above is removed too, whose span ends where this one starts.
    /// </summary>
    private Span WithoutFinalNewline(Span span, IReadOnlySet<ReadElement> removed)
    {
        if (span.End != Text.Length || span.Start == 0 || Text.Bytes[^1] == (byte)'\n' || Text.Bytes[span.Start - 1] != (byte)'\n') return span;
        if (removed.Any(other => other.Entry is TreeEntry entry && (entry.LineSpan ?? entry.Own).End == span.Start)) return span;
        var start = span.Start - 1;
        if (start > 0 && Text.Bytes[start - 1] == (byte)'\r') start--;
        return span with { Start = start };
    }
}
