using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>
/// The <c>yaml</c> family (FBL §4.3): the <see cref="YamlParser"/> reads the stream once, YamlDotNet
/// deciding whether it is well-formed and its tokens giving the first document's structure and spans,
/// and writing keeps each replaced scalar's style and the body's indentation (FBL §6.3).
/// </summary>
internal sealed class YamlFamily(BodyText text, FblBinding binding, FblOptions options) : TreeFamily(text, binding, options)
{
    private List<Span> _leaves = [];

    // The attribute bindings the host says hold a date or a date-time, found once by reference.
    private readonly HashSet<AttributeBinding> _timeTyped = [.. binding.AllRules
        .SelectMany(rule => rule.Attributes.Where(attribute => options.TimeAttributes.Contains($"{rule.Name}.{attribute.Key}")).Select(attribute => attribute.Value))];

    // The attributes read as text (FBL 0.5 §5.2): a value written for one reads back as the same text.
    private readonly HashSet<AttributeBinding> _textRead = [.. binding.AllRules
        .SelectMany(rule => rule.Attributes.Where(attribute => ReadModes.Of(rule, attribute.Key, attribute.Value, options) == ReadMode.Text).Select(attribute => attribute.Value))];

    public override string FamilyName => "yaml";

    protected override IReadOnlyList<Span> Leaves => _leaves;

    public override void Parse()
    {
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
        // An entry inside a flow collection has no line span of its own, whatever lines it stands on (FBL 0.10 §4.3).
        foreach (var entry in AllEntries.OfType<TreeEntry>().Where(e => e is { IsRoot: false, InFlow: false })) entry.LineSpan = LineSpanOf(entry);
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
        // A number or a bool read from a quoted scalar (FBL 0.5 quoted: convert) keeps its quotes.
        if (binding?.Quoted == QuotedRule.Convert && old?.Style is ValueStyle.Single or ValueStyle.Double && (wire ?? value) is bool or long or int or double or decimal)
        {
            written = old.Style == ValueStyle.Single ? YamlScalars.SingleQuoted(written) : YamlScalars.DoubleQuoted(written);
        }
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
            case WrittenStyle.Single when !multiline: return YamlScalars.SingleQuoted(value);
            case WrittenStyle.Double: return YamlScalars.DoubleQuoted(value);
            case WrittenStyle.Literal when multiline: return Literal(value, keyIndent);
            case WrittenStyle.Plain when YamlScalars.IsPlainWritable(value, timeTyped) || (_textRead.Contains(binding) && YamlScalars.IsPlainSyntax(value)): return value;
        }
        if (YamlScalars.IsPlainSafe(value, timeTyped)) return value;
        return Binding.Text.Quote == QuoteStyle.Single && !multiline ? YamlScalars.SingleQuoted(value) : YamlScalars.DoubleQuoted(value);
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

    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Read.Node is not TreeEntry member) return true;

        // FBL 0.4, section 5.2: the levels the key leaves empty go with it, up to the entry itself.
        if (change.Binding is { Create.AtEnd: true, Child: { } reached })
        {
            RemoveEmptiedLevels(plan, member, reached.Trim('/').Split('/').Length, entry => entry == member);
        }
        plan.Add(SpliceOperation.RemoveKey, member.LineSpan ?? member.Own, "");
        return true;
    }

    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        if (change.Read.Node is not TreeEntry member) return false;
        var written = Format(change.Read, change.Binding, change.Value);
        if (written != Text.Text(member.Value.Span)) plan.Add(SpliceOperation.ReplaceValue, member.Value.Span, written);
        return true;
    }

    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        var mapping = change.Read.Node as TreeValue ?? Mapping((TreeEntry)element.Entry, change.Binding.Child);
        if (mapping is null && change.Binding is { Create.AtEnd: true, Child: { } child, Kind: SlotKind.Key(var created) })
        {
            // FBL 0.4, section 5.2: every missing level of the child mapping, then the key, at one offset.
            if (!MissingLevels((TreeEntry)element.Entry, child.Trim('/').Split('/'), out var at, out var levels, out var inside))
            {
                Plan.Refuse($"The {Binding.Name} file has no place to create {child} in.");
                return true;
            }
            foreach (var level in levels) plan.Add(SpliceOperation.EnsureContainer, at, at, NewLine(at, [level]));
            plan.Add(SpliceOperation.InsertKey, at, at, NewLine(at, [Indentation(inside) + created + ": " + Scalar(change.Value, null, change.Binding, inside)]));
            return true;
        }
        var key = change.Binding.Kind is SlotKind.Key(var name) ? name : null;
        if (mapping is null || mapping.Kind != ValueKind.Mapping || key is null)
        {
            Plan.Refuse($"The {Binding.Name} file has no mapping to write '{key}' in.");
            return true;
        }
        InsertKey(plan, mapping, key, change);
        return true;
    }

    /// <summary>A key the entry lacks goes at its place in the rule's key order: after the nearest present key before it, else before the nearest after it.</summary>
    private void InsertKey(Plan plan, TreeValue mapping, string key, SlotChange change)
    {
        var order = change.Rule.KeyOrder(change.Binding.Child);
        var index = order.IndexOf(key);
        int offset;
        TreeEntry? before = null;
        for (var i = index - 1; i >= 0 && before is null; i--) before = mapping.Member(order[i]);
        if (before is not null)
        {
            offset = LineEndAfter(before);
        }
        else if (change.Binding.Create?.AtEnd == true)
        {
            // FBL 0.4, section 5.2: with no key of the rule before it, the mapping's first key.
            offset = mapping.Entries[0].LineSpan?.Start ?? mapping.Entries[0].Own.Start;
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
        if (insert.Place == InsertPlace.Before) Plan.Refuse($"A {FamilyName} body cannot place a new entry '{WireName.Of(insert.Place)}'.");
        var parent = request.Parent?.Entry ?? EndParent(request);
        var captures = request.Parent?.Candidate.Captures ?? (parent is null ? null : plan.Reading.ElementOf(parent)?.Candidate.Captures) ?? CapturesOf(plan);
        var selector = insert.Container ?? ContainerOf(rule.At);
        var container = Container(selector, parent, captures);
        var member = MemberKey(request);
        var keys = ItemLines(request);
        if (keys.Count == 0)
        {
            Plan.Refuse($"A new {rule.Type} would have no keys to write.");
            return;
        }
        if (container is null)
        {
            EnsureContainer(plan, insert, selector, parent, keys, member);
            return;
        }
        if (member is not null)
        {
            InsertMember(plan, request, container, selector, member, keys);
            return;
        }
        var value = container.Value;
        if (value.Style is ValueStyle.FlowSequence or ValueStyle.FlowMapping && !(value is { Style: ValueStyle.FlowSequence, Flow: List<object?> { Count: 0 } } && container.KeySpan is not null))
        {
            InsertIntoFlow(plan, request, container, selector);
            return;
        }
        if (value.Kind == ValueKind.Sequence)
        {
            var siblings = value.Entries;
            var previous = insert.Place switch
            {
                InsertPlace.Start => null,
                InsertPlace.AfterLast => siblings.LastOrDefault(s => plan.Reading.ClaimedBy(s) == rule.Name) ?? siblings.LastOrDefault(),
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

    /// <summary>
    /// A new member of a mapping, named by its key (FBL 0.14 §6.2): the key, then the entry's keys and skeleton one step
    /// deeper, placed among the container's members by <c>place</c> and indented like the member it follows, else like
    /// the one it precedes, else one step deeper than the container's key. A container written as its key alone takes it
    /// as the first member of a block mapping; one written in flow form, or that is not a mapping, takes none.
    /// </summary>
    private void InsertMember(Plan plan, InsertRequest request, TreeEntry container, string selector, string key, List<string> lines)
    {
        var value = container.Value;
        var name = selector.TrimEnd('/')[(selector.TrimEnd('/').LastIndexOf('/') + 1)..];
        if (value.Style == ValueStyle.FlowMapping) Plan.Refuse($"The {name} are written as a flow mapping, which takes no insert.");
        if (value.Style == ValueStyle.Empty)
        {
            var at = container.IsRoot ? Text.Length : LineEndAfter(container);
            plan.Add(SpliceOperation.InsertEntry, at, at, NewLine(at, MemberLines(key, lines, container.IsRoot ? 0 : container.Indent + Step)));
            return;
        }
        if (value is not { Kind: ValueKind.Mapping, Entries.Count: > 0 }) Plan.Refuse($"The {selector} of this file is not a mapping a {request.Rule.Type} can be added to.");
        RefuseDuplicate(request, value, key);
        var siblings = value.Entries;
        var previous = request.Rule.Insert!.Place switch
        {
            InsertPlace.Start => null,
            InsertPlace.AfterLast => siblings.LastOrDefault(s => plan.Reading.ClaimedBy(s) == request.Rule.Name) ?? siblings[^1],
            _ => siblings[^1],
        };
        var offset = previous is null ? siblings[0].LineSpan?.Start ?? siblings[0].Own.Start : LineEndAfter(previous);
        plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, MemberLines(key, lines, (previous ?? siblings[0]).Indent)));
    }

    /// <summary>
    /// The lines of a new member (FBL 0.14 §6.2): its key at <paramref name="indent"/>, written as a new string value is,
    /// plain when it is plain-safe and else in <c>text.quote</c>, then <paramref name="lines"/> one step deeper.
    /// </summary>
    private List<string> MemberLines(string key, List<string> lines, int indent)
    {
        var inside = indent + Step;
        var member = new List<string>(lines.Count + 1) { Indentation(indent) + String(key, null, null, indent) + ":" };
        member.AddRange(lines.Select(line => Indentation(inside) + Nested(line, inside)));
        return member;
    }

    /// <summary>
    /// An insert into a flow collection that holds something (FBL 0.10 §6.2, Flow sequences): a one-line flow sequence
    /// takes the new item inside it, after its last item and a <c>, </c>, written in flow form; a flow sequence over
    /// several lines and a flow mapping take no insert, and the add is refused with a reason.
    /// </summary>
    private void InsertIntoFlow(Plan plan, InsertRequest request, TreeEntry container, string selector)
    {
        var value = container.Value;
        var name = selector.TrimEnd('/')[(selector.TrimEnd('/').LastIndexOf('/') + 1)..];
        if (value.Style == ValueStyle.FlowMapping) Plan.Refuse($"The {name} are written as a flow mapping, which takes no insert.");
        if (Text.LineIndexAt(value.Span.Start) != Text.LineIndexAt(value.Span.End - 1)) Plan.Refuse($"The {name} list is written over several lines, so nothing can be added inside it.");
        if (request.Rule.Insert!.Skeleton is not null) Plan.Refuse($"A new {request.Rule.Type} has a skeleton, which cannot be written inside the one-line {name} list.");
        var item = "{" + string.Join(", ", InsertValues(request).Select(FlowMember)) + "}";
        var offset = value.Entries[^1].Own.End;
        plan.Add(SpliceOperation.InsertEntry, offset, offset, ", " + item);
    }

    /// <summary>A key of a new item of a flow sequence and its value in flow form: a level of child as a flow mapping of its members.</summary>
    private string FlowMember(InsertedValue inserted)
    {
        if (inserted.Members is { } members) return $"{FlowItem(inserted.Key)}: {{{string.Join(", ", members.Select(FlowMember))}}}";
        var value = inserted.Binding is null ? inserted.Value : NewText.Wire(inserted.Binding, inserted.Value, null) ?? inserted.Value;
        return $"{FlowItem(inserted.Key)}: {(value is string text ? FlowItem(text) : Scalar(value, null, inserted.Binding, 0))}";
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
        return request.Rule.Target?.Kind is SlotKind.Parent
            ? request.Target?.Entry
            : request.Rule.Source?.Kind is SlotKind.Parent
                ? request.Source?.Entry
                : null;
    }

    private void EnsureContainer(Plan plan, InsertSettings insert, string selector, Entry? parent, List<string> keys, string? member)
    {
        if (insert.Create is not { } create)
        {
            Plan.Refuse(insert.ContainerRefusal ?? $"The file has no {selector.TrimStart('/')} to add to.");
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
            case ContainerPlace.End:
            {
                // FBL 0.4, section 6.2, Missing levels: outermost first, then the entry, all at one offset.
                var from = selector.StartsWith('/') ? Root : parent as TreeEntry;
                if (from is null || !MissingLevels(from, selector.Trim('/').Split('/'), out var at, out var levels, out var inside))
                {
                    Plan.Refuse($"The file has no place to create {name} in.");
                    return;
                }
                if (create.Text is { } own && levels.Count > 0) levels[^1] = own;
                foreach (var level in levels) plan.Add(SpliceOperation.EnsureContainer, at, at, NewLine(at, [level]));
                var first = inside - Step + SequenceOffset;
                plan.Add(SpliceOperation.InsertEntry, at, at, NewLine(at, member is null ? Item(keys, first, first + 2) : MemberLines(member, keys, inside)));
                return;
            }
            // At the end of a document whose root is a mapping, or that holds nothing yet: a root list or scalar has no
            // key to write the container beside, and one written after it would leave a file that is not YAML.
            case ContainerPlace.EndOfDocument when mapping is null or { Kind: ValueKind.Mapping } or { Style: ValueStyle.Empty }:
                offset = Text.Length;
                indent = mapping is { Kind: ValueKind.Mapping, Entries.Count: > 0 } ? mapping.Entries[0].Indent : 0;
                break;
            case ContainerPlace.After when mapping?.Member(create.Argument!) is { } after:
                offset = LineEndAfter(after);
                indent = after.Indent;
                break;
            case ContainerPlace.Before when mapping?.Member(create.Argument!) is { } before:
                offset = before.LineSpan?.Start ?? before.Own.Start;
                indent = before.Indent;
                break;
            // Before a key the file does not have: at its end, as after-last falls back to end.
            case ContainerPlace.Before when mapping is { Kind: ValueKind.Mapping, Entries.Count: > 0 }:
                offset = Text.Length;
                indent = mapping.Entries[0].Indent;
                break;
            case ContainerPlace.Under when Container(create.Argument!, parent, new Dictionary<string, string>()) is { Value: { Kind: ValueKind.Mapping, Entries.Count: > 0 } under }:
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
        plan.Add(SpliceOperation.InsertEntry, offset, offset, NewLine(offset, member is null ? Item(keys, dash, dash + 2) : MemberLines(member, keys, indent + Step)));
    }

    /// <summary>
    /// The levels of <paramref name="segments"/> that <paramref name="from"/> does not reach, as the key
    /// lines that create them, outermost first; where they go, which is the end of the innermost level
    /// the body has; and the indentation of what the innermost level holds (FBL 0.4, section 6.2).
    /// False when a missing level is named by <c>*</c> or a capture, which gives no key to write, or
    /// when the level to write in is not a block mapping.
    /// </summary>
    private bool MissingLevels(TreeEntry from, IReadOnlyList<string> segments, out int offset, out List<string> lines, out int inside)
    {
        offset = 0;
        inside = 0;
        lines = [];
        var level = from;
        var reached = 0;
        while (reached < segments.Count && level.Value.Kind == ValueKind.Mapping && level.Value.Member(segments[reached]) is { } member)
        {
            level = member;
            reached++;
        }
        int indent;
        if (level.Value is { Kind: ValueKind.Mapping, Entries.Count: > 0 } mapping)
        {
            offset = level.IsRoot ? Text.Length : LineEndAfter(mapping.Entries[^1]);
            indent = mapping.Entries[0].Indent;
        }
        else if (level.Value.Style == ValueStyle.Empty)
        {
            offset = level.IsRoot ? Text.Length : LineEndAfter(level);
            indent = level.IsRoot ? 0 : level.Indent + Step;
        }
        else
        {
            return false;
        }
        for (var i = reached; i < segments.Count; i++)
        {
            if (segments[i] == "*" || segments[i].StartsWith('{')) return false;
            lines.Add(Indentation(indent) + segments[i] + ":");
            indent += Step;
        }
        inside = indent;
        return true;
    }

    /// <summary>
    /// Removes the heads of the levels above <paramref name="start"/> that its removal leaves empty,
    /// at most <paramref name="most"/> of them and never the root, outermost first (FBL 0.4, section 6.2).
    /// A level is left empty when everything in it is <paramref name="going"/> or is such a level itself.
    /// </summary>
    private void RemoveEmptiedLevels(Plan plan, TreeEntry start, int most, Func<TreeEntry, bool> going)
    {
        var heads = new List<Span>();
        TreeEntry? emptied = null;
        var level = start.Parent as TreeEntry;
        while (heads.Count < most && level is { IsRoot: false, LineSpan: { } line } && level.Value.Entries.All(entry => entry == emptied || going(entry)))
        {
            var first = level.Value.Entries[0];
            heads.Insert(0, line with { End = (first.LineSpan ?? first.Own).Start });
            emptied = level;
            level = level.Parent as TreeEntry;
        }
        foreach (var head in heads)
        {
            if (!plan.Touches(head)) plan.Add(SpliceOperation.RemoveContainer, head, "");
        }
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

    /// <summary>The key lines of a new item in the rule's key order, then the skeleton's lines.</summary>
    private List<string> ItemLines(InsertRequest request)
    {
        var rule = request.Rule;
        var lines = new List<string>();
        foreach (var inserted in InsertValues(request)) AddLines(lines, inserted, 0);
        if (rule.Insert!.Skeleton is { } skeleton)
        {
            var rendered = NewText.Render(skeleton, name => request.Values.TryGetValue(name, out var v) && v is not null ? NewText.Plain(v, rule.Attribute(name)) : name == "id" ? request.Id : null);
            lines.AddRange(rendered.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n'));
        }
        return lines;
    }

    /// <summary>
    /// The lines of one key of a new entry, <paramref name="depth"/> steps under the entry's keys: a key with its value,
    /// or a level of child (FBL 0.10 §6.2), its key with what its mapping holds one step deeper below it.
    /// </summary>
    private void AddLines(List<string> lines, InsertedValue inserted, int depth)
    {
        var indent = Indentation(depth * Step);
        if (inserted.Members is { } members)
        {
            lines.Add($"{indent}{inserted.Key}:");
            foreach (var member in members) AddLines(lines, member, depth + 1);
            return;
        }
        var value = inserted.Binding is null ? inserted.Value : NewText.Wire(inserted.Binding, inserted.Value, null) ?? inserted.Value;
        lines.Add($"{indent}{inserted.Key}: {Scalar(value, null, inserted.Binding, depth * Step)}");
    }

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed)
    {
        var entry = (TreeEntry)element.Entry;
        if (entry.IsRoot)
        {
            Plan.Refuse($"The whole {Binding.Name} file cannot be removed.");
            return;
        }
        if (entry.InFlow)
        {
            RemoveFromFlow(plan, element, entry);
            return;
        }
        var span = entry.LineSpan ?? entry.Own;
        if (element.Rule.Remove is { RemoveContainerWhenEmpty: true } remove)
        {
            // The container alone, or with remove-empty-levels every level of its selector that a key
            // names, counted from the container up to the first level a * or a capture names.
            var levels = 1;
            if (remove.RemoveEmptyLevels)
            {
                var segments = (element.Rule.Insert?.Container ?? ContainerOf(element.Rule.At)).Trim('/').Split('/');
                levels = segments.Reverse().TakeWhile(segment => segment.Length > 0 && segment != "*" && !segment.StartsWith('{')).Count();
            }
            var gone = removed.Where(r => r.Rule.Remove?.RemoveContainerWhenEmpty == true).Select(r => r.Entry).ToHashSet();
            // A sibling container goes too when this edit removes everything in it.
            RemoveEmptiedLevels(plan, entry, levels, other =>
                removed.Any(r => r.Entry == other) || (other.Value.Entries.Count > 0 && other.Value.Entries.All(gone.Contains)));
        }
        plan.Add(SpliceOperation.RemoveEntry, WithoutFinalNewline(span, removed), "");
    }

    /// <summary>
    /// A removal of an item of a flow sequence (FBL 0.10 §6.2, Flow sequences): the item and one separator, the
    /// <c>,</c> after it and the whitespace up to the next item, or, for the last item, the <c>,</c> before it and the
    /// whitespace between. The only item is removed alone, leaving <c>[]</c>, and under <c>remove-when-empty</c> its
    /// container goes with it, as the key's line before the item and after it, around the item's removal.
    /// </summary>
    private void RemoveFromFlow(Plan plan, ReadElement element, TreeEntry entry)
    {
        if (entry.Parent is not TreeEntry { Value.Style: ValueStyle.FlowSequence } container)
        {
            Plan.Refuse($"This {element.Rule.Type} is written inside a flow collection it cannot be taken out of.");
            return;
        }
        var siblings = container.Value.Entries;
        var index = siblings.IndexOf(entry);
        if (siblings.Count == 1)
        {
            if (element.Rule.Remove is { RemoveContainerWhenEmpty: true } && container.LineSpan is { } line)
            {
                plan.Add(SpliceOperation.RemoveContainer, line.Start, entry.Own.Start, "");
                plan.Add(SpliceOperation.RemoveEntry, entry.Own, "");
                plan.Add(SpliceOperation.RemoveContainer, entry.Own.End, line.End, "");
                return;
            }
            plan.Add(SpliceOperation.RemoveEntry, entry.Own, "");
            return;
        }
        var span = index == siblings.Count - 1
            ? entry.Own with { Start = siblings[index - 1].Separator }
            : entry.Own with { End = siblings[index + 1].Own.Start };
        plan.Add(SpliceOperation.RemoveEntry, span, "");
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
