using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Json;

/// <summary>
/// The <c>json</c> family (FBL §4.4): an RFC 8259 text read byte for byte, members and items with
/// their own spans and the separator after each, comments refused, duplicate names reported.
/// </summary>
internal sealed class JsonFamily(BodyText text, FblBinding binding, FblOptions options) : TreeFamily(text, binding, options)
{
    private List<Span> _leaves = [];

    public override string FamilyName => "json";

    protected override IReadOnlyList<Span> Leaves => _leaves;

    protected override bool IsTrivia(Span gap)
    {
        for (var i = gap.Start; i < gap.End; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'{' or (byte)'}' or (byte)'[' or (byte)']' or (byte)',' or (byte)':')) return false;
        }
        return true;
    }

    public override void Parse()
    {
        var parser = new JsonBodyParser(Text);
        _leaves = parser.Leaves;
        try
        {
            var value = parser.Parse();
            foreach ((string name, Span span) in parser.Duplicates) Report(FindingCodes.DuplicateKey, FindingSeverity.Warning, $"The member \"{name}\" appears again in this object; only the first is read.", span);
            Root = new TreeEntry { IsRoot = true, Own = value.Span, Value = value, Indent = Column(value.Span.Start) };
            Index(Root);
            foreach (var entry in AllEntries.OfType<TreeEntry>().Where(e => !e.IsRoot)) entry.LineSpan = LineSpanOf(entry);
        }
        catch (JsonBodyParser.JsonError error)
        {
            Unreadable = (error.Offset, error.Message);
        }
    }

    private int Column(int offset)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        return offset - Math.Max(line.Start, Text.LineIndexAt(offset) == 0 ? Text.BomLength : 0);
    }

    /// <summary>
    /// An entry starts its line when only whitespace precedes it, and ends it when only its
    /// separator and whitespace follow; its line span then takes the separator with it.
    /// </summary>
    private Span? LineSpanOf(TreeEntry entry)
    {
        if (!OnlyWhitespaceBefore(entry.Own.Start)) return null;
        var i = entry.Own.End;
        var line = Text.Lines[Text.LineIndexAt(Math.Max(entry.Own.Start, i - 1))];
        while (i < line.ContentEnd && Text.Bytes[i] is (byte)' ' or (byte)'\t') i++;
        if (i < line.ContentEnd && Text.Bytes[i] == (byte)',') i++;
        while (i < line.ContentEnd && Text.Bytes[i] is (byte)' ' or (byte)'\t') i++;
        if (i != line.ContentEnd) return null;
        return new Span(Text.Lines[Text.LineIndexAt(entry.Own.Start)].Start, line.End);
    }

    // ---- writing ----

    public override string Format(SlotRead read, AttributeBinding? binding, object? value)
    {
        var wire = binding is null ? null : NewText.Wire(binding, value, read.Wire);
        return wire is not null ? Quote(wire) : Write(value, binding);
    }

    private static string Write(object? value, AttributeBinding? binding) => value switch
    {
        null => "null",
        string s => Quote(s),
        bool b => b ? "true" : "false",
        _ when NewText.TryNumber(value, out var n) => NewText.Number(n, binding?.Decimals),
        IEnumerable<object?> list => "[" + string.Join(", ", list.Select(v => Write(v, binding))) + "]",
        _ => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    /// <summary>A JSON string with the escapes RFC 8785 uses (FBL §6.3).</summary>
    public static string Quote(string value) => "\"" + Escape(value) + "\"";

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    protected override bool RemoveSlot(Plan plan, ReadElement element, SlotChange change)
    {
        if (change.Read.Node is TreeEntry member) plan.Add(SpliceOperation.RemoveKey, RemovalSpan(member), "");
        return true;
    }

    protected override bool ReplaceSlot(Plan plan, SlotChange change)
    {
        if (change.Read.Span is not { } span) return false;
        var written = Format(change.Read, change.Binding, change.Value);
        if (written != Text.Text(span)) plan.Add(SpliceOperation.ReplaceValue, span, written);
        return true;
    }

    protected override bool InsertSlot(Plan plan, ReadElement element, SlotChange change)
    {
        var mapping = Mapping((TreeEntry)element.Entry, change.Binding.Child);
        var key = change.Binding.Kind is SlotKind.Key(var name) ? name : null;
        if (mapping is null || key is null)
        {
            Plan.Refuse($"The {Binding.Name} file has no object to write \"{key}\" in.");
            return true;
        }
        var added = $"{Quote(key)}: {Format(change.Read, change.Binding, change.Value)}";
        (int offset, string text) = MemberBefore(mapping, key, change.Rule.WireOrder()) is { } before
            ? (before.Own.End, (before.LineSpan is not null ? "," + NewlineAt(before.Own.End) + Indentation(before.Indent) : ", ") + added)
            : NewMember(mapping, added);
        plan.Add(SpliceOperation.InsertKey, offset, offset, text);
        return true;
    }

    /// <summary>
    /// The member a new <paramref name="key"/> is written right after: the nearest one before it in
    /// the rule's key order (FBL §6.1, "at the position <c>insert.keys</c> gives"). Null when the
    /// order does not place the key, or places it after every member, and it goes last.
    /// </summary>
    private static TreeEntry? MemberBefore(TreeValue mapping, string key, IReadOnlyList<string>? order)
    {
        if (order is null) return null;
        var position = PlaceIn(order, key);
        if (position < 0) return null;
        var before = mapping.Entries.LastOrDefault(entry => entry.Name is { } name && PlaceIn(order, name) is >= 0 and var at && at < position);
        return before is null || before == mapping.Entries[^1] ? null : before;
    }

    /// <summary>The place of <paramref name="key"/> in a rule's key order, or -1 when the order does not name it.</summary>
    private static int PlaceIn(IReadOnlyList<string> order, string key)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == key) return index;
        }
        return -1;
    }

    /// <summary>A new member or item after a container's last entry, with the previous entry's separator (FBL §6.3).</summary>
    private (int Offset, string Text) NewMember(TreeValue container, string text)
    {
        if (container.Entries.Count == 0) return (container.Span.Start + 1, text);
        var last = container.Entries[^1];
        return last.LineSpan is not null ? (last.Own.End, "," + NewlineAt(last.Own.End) + Indentation(last.Indent) + text) : (last.Own.End, ", " + text);
    }

    public override void Insert(Plan plan, InsertRequest request)
    {
        var insert = request.Rule.Insert!;
        // FBL §5's place: {before: key} is not implemented here yet; refused, as the lines family does, rather than placed at the end.
        if (insert.Place == InsertPlace.Before) Plan.Refuse($"A {FamilyName} body cannot place a new entry '{WireName.Of(insert.Place)}'.");
        var container = insert.Container is null ? null : Container(insert.Container, request.Parent?.Entry, CapturesOf(plan));
        if (MemberKey(request) is { } member)
        {
            InsertMember(plan, request, container, member);
            return;
        }
        string text;
        if (insert.Emit is { } emit)
        {
            text = NewText.Render(emit, name => name switch
            {
                "id" => request.Id is null ? null : Escape(request.Id),
                "source" => request.Source is null ? null : Escape(request.Source.Key),
                "target" => request.Target is null ? null : Escape(request.Target.Key),
                _ => request.Values.TryGetValue(name, out var v) && v is not null ? Escape(NewText.Plain(v, request.Rule.Attribute(name))) : null,
            });
        }
        else
        {
            var members = InsertValues(request).Select(Member).ToList();
            IReadOnlyList<TreeEntry> entries = container is null ? [] : container.Value.Entries;
            var neighbour = entries.Count == 0 ? null : insert.Place == InsertPlace.Start ? entries[0] : entries[^1];
            text = NewObject(members, neighbour);
        }
        if (container is null)
        {
            if (NewContainer(insert, request.Parent?.Entry as TreeEntry) is not { } created)
            {
                Plan.Refuse(insert.Create is null && insert.ContainerRefusal is { } refusal ? refusal : $"The {Binding.Name} file has no {insert.Container ?? "container"} to add the {request.Rule.Type} to.");
                return;
            }

            // A container that encloses its entries on one line cannot be written before an entry that
            // goes inside it, so it is created as its opening and its closing with the entry between
            // them, all three at one offset (FBL §6.2).
            plan.Add(SpliceOperation.EnsureContainer, created.Offset, created.Offset, created.Opening);
            plan.Add(SpliceOperation.InsertEntry, created.Offset, created.Offset, text);
            plan.Add(SpliceOperation.EnsureContainer, created.Offset, created.Offset, "]");
            return;
        }
        if (container.Value.Kind == ValueKind.Scalar)
        {
            Plan.Refuse($"The {Binding.Name} file has no {insert.Container ?? "container"} to add the {request.Rule.Type} to.");
            return;
        }
        if (insert.Place == InsertPlace.Start && container.Value.Entries.Count > 0)
        {
            var first = container.Value.Entries[0];
            var newline = first.LineSpan is not null;
            plan.Add(SpliceOperation.InsertEntry, first.Own.Start, first.Own.Start, text + (newline ? "," + NewlineAt(first.Own.Start) + Indentation(first.Indent) : ", "));
            return;
        }
        (int offset, string inserted) = NewMember(container.Value, text);
        plan.Add(SpliceOperation.InsertEntry, offset, offset, inserted);
    }

    /// <summary>
    /// A new member of an object, named by its key (FBL 0.14 §6.2): the key as a json string, <c>: </c> and an object of
    /// the entry's keys, one member per line as its siblings' are, after the rule's last member for <c>after-last</c>,
    /// else after the last member or before the first, with the separator its place needs. A container that is missing
    /// is not created here, as this family creates no missing level (FBL 0.4); and a skeleton, which is text of another
    /// family's, is not written into a json body, so an insert that has one is refused rather than left incomplete.
    /// </summary>
    private void InsertMember(Plan plan, InsertRequest request, TreeEntry? container, string key)
    {
        var insert = request.Rule.Insert!;
        if (container is null)
        {
            Plan.Refuse(insert.ContainerRefusal ?? $"The {Binding.Name} file has no {insert.Container} to add the {request.Rule.Type} to.");
            return;
        }
        if (container.Value.Kind != ValueKind.Mapping) Plan.Refuse($"The {insert.Container} of this file is not an object a {request.Rule.Type} can be added to.");
        if (insert.Skeleton is not null) Plan.Refuse($"A new {request.Rule.Type} is written with a skeleton, which a {FamilyName} file cannot hold.");
        RefuseDuplicate(request, container.Value, key);
        var siblings = container.Value.Entries;
        var previous = siblings.Count == 0 ? null : insert.Place switch
        {
            InsertPlace.Start => null,
            InsertPlace.AfterLast => siblings.LastOrDefault(s => plan.Reading.ClaimedBy(s) == request.Rule.Name) ?? siblings[^1],
            _ => siblings[^1],
        };
        var members = InsertValues(request).Select(Member).ToList();
        var text = Quote(key) + ": " + NewObject(members, previous ?? siblings.FirstOrDefault());
        if (previous is null && siblings.Count > 0)
        {
            var first = siblings[0];
            plan.Add(SpliceOperation.InsertEntry, first.Own.Start, first.Own.Start, text + (first.LineSpan is not null ? "," + NewlineAt(first.Own.Start) + Indentation(first.Indent) : ", "));
            return;
        }
        if (previous is null)
        {
            plan.Add(SpliceOperation.InsertEntry, container.Value.Span.Start + 1, container.Value.Span.Start + 1, text);
            return;
        }
        var separator = previous.LineSpan is not null ? "," + NewlineAt(previous.Own.End) + Indentation(previous.Indent) : ", ";
        plan.Add(SpliceOperation.InsertEntry, previous.Own.End, previous.Own.End, separator + text);
    }

    /// <summary>A member of a new entry's object: a key with its value, or a level of child (FBL 0.10 §6.2) as a member whose value is an object of what it holds.</summary>
    private static string Member(InsertedValue inserted) =>
        inserted.Members is { } members
            ? $"{Quote(inserted.Key)}: {{ {string.Join(", ", members.Select(Member))} }}"
            : $"{Quote(inserted.Key)}: {Write(inserted.Value, inserted.Binding)}";

    /// <summary>
    /// Where a missing container is created and how it opens (FBL §6.2 <c>create</c>): a list under
    /// the selector's last name, right after the member <c>create.at.after</c> names in the parent
    /// entry's object, or after that object's last member for <c>create.at: "end"</c>. Null when the rule creates no container, or creates it in a way this family
    /// does not write yet - anywhere but after a member that stands on one line with its siblings.
    /// </summary>
    private static (int Offset, string Opening)? NewContainer(InsertSettings insert, TreeEntry? parent)
    {
        if (insert is not { Container: { } selector, Create: { At: ContainerPlace.After or ContainerPlace.End, Text: null } create } || selector.StartsWith('/')) return null;
        if (parent?.Value is not { Kind: ValueKind.Mapping, Entries.Count: > 0 } mapping) return null;

        // "end" (FBL 0.4) is after the last member of the container's parent; "after" names the member.
        var member = create.At == ContainerPlace.End ? mapping.Entries[^1] : create.Argument is { } after ? mapping.Member(after) : null;
        if (member is not { LineSpan: null }) return null;
        var name = selector.TrimEnd('/')[(selector.TrimEnd('/').LastIndexOf('/') + 1)..];
        return (member.Own.End, $", {Quote(name)}: [");
    }

    /// <summary>
    /// A new entry's object in the layout of <paramref name="neighbour"/>, the entry it is written beside
    /// (FBL §6.3, spec 025 B-12): a member per line, indented as the neighbour's members are, when the
    /// neighbour's members stand on lines of their own; else on one line.
    /// </summary>
    private string NewObject(IReadOnlyList<string> members, TreeEntry? neighbour)
    {
        if (members.Count == 0 || neighbour is not { Value: { Kind: ValueKind.Mapping, Entries: [{ LineSpan: not null } first, ..] } })
        {
            return "{ " + string.Join(", ", members) + " }";
        }
        var newline = NewlineAt(first.Own.Start);
        var indent = Indentation(first.Indent);
        return "{" + newline + indent + string.Join("," + newline + indent, members) + newline + Indentation(neighbour.Indent) + "}";
    }

    /// <summary>
    /// What removing an entry takes (FBL §6.2): its line span when it has one and is not the last of
    /// several, else its own span with one separator: the one after it, or, for the last entry, the
    /// one before it and the whitespace in between.
    /// </summary>
    private Span RemovalSpan(TreeEntry entry)
    {
        var siblings = ((TreeEntry)entry.Parent!).Value.Entries;
        var index = siblings.IndexOf(entry);
        var isLast = index == siblings.Count - 1;
        if (isLast && index > 0)
        {
            var previous = siblings[index - 1];
            return entry.Own with { Start = previous.Separator };
        }
        if (entry.LineSpan is { } line) return line;
        if (!isLast) return entry.Own with { End = siblings[index + 1].Own.Start };
        return entry.Own;
    }

    /// <summary>
    /// A move among siblings in json (FBL §5.5): the entry is removed with the one separator a
    /// removal takes, and its own bytes are inserted with the separator its new place needs - after
    /// it when another entry follows, before it when it becomes the last.
    /// </summary>
    public override void Move(Plan plan, ReadElement element, ReadElement? before, ReadElement last)
    {
        var entry = (TreeEntry)element.Entry;
        var own = Text.Text(entry.Own);
        plan.Add(SpliceOperation.RemoveEntry, RemovalSpan(entry), "");
        if (before is not null)
        {
            var next = (TreeEntry)before.Entry;
            if (next.LineSpan is { } line)
            {
                plan.Add(SpliceOperation.InsertEntry, line.Start, line.Start, Indentation(next.Indent) + own + "," + NewlineAt(line.Start));
                return;
            }
            plan.Add(SpliceOperation.InsertEntry, next.Own.Start, next.Own.Start, own + ", ");
            return;
        }
        var tail = (TreeEntry)last.Entry;
        var inserted = tail.LineSpan is not null
            ? "," + NewlineAt(tail.Own.End) + Indentation(tail.Indent) + own
            : ", " + own;
        plan.Add(SpliceOperation.InsertEntry, tail.Own.End, tail.Own.End, inserted);
    }

    public override void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed)
    {
        var entry = (TreeEntry)element.Entry;
        if (entry.IsRoot)
        {
            Plan.Refuse($"The whole {Binding.Name} file cannot be removed.");
            return;
        }

        // The only entry of a container that goes when it is left empty (FBL §6.2): the container
        // goes with it, as what is before the entry, the entry itself, and what is after it.
        if (element.Rule.Remove?.RemoveContainerWhenEmpty == true &&
            entry.Parent is TreeEntry { IsRoot: false, Parent: TreeEntry, Value.Entries.Count: 1 } container)
        {
            // The container goes as it was created (FBL §6.2): with the separator before it, where a
            // member stands before it, so that removing what an insert created leaves the bytes the
            // insert started from. Only a first member takes the separator after it instead.
            var members = ((TreeEntry)container.Parent!).Value.Entries;
            var at = members.IndexOf(container);
            var start = at > 0 ? members[at - 1].Own.End : container.Own.Start;
            var end = at == 0 && members.Count > 1 ? members[1].Own.Start : container.Own.End;
            plan.Add(SpliceOperation.RemoveContainer, start, entry.Own.Start, "");
            plan.Add(SpliceOperation.RemoveEntry, entry.Own, "");
            plan.Add(SpliceOperation.RemoveContainer, entry.Own.End, end, "");
            return;
        }

        plan.Add(SpliceOperation.RemoveEntry, RemovalSpan(entry), "");
    }
}
