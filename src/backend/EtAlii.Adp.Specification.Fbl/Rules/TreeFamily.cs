using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Rules;

internal enum ValueKind
{
    Scalar,
    Mapping,
    Sequence,
}

/// <summary>How a value is written: FBL §6.3 keeps a replaced value's style when the new value fits it.</summary>
internal enum ValueStyle
{
    Plain,
    Single,
    Double,
    Literal,
    Folded,
    FlowSequence,
    FlowMapping,
    Block,
    Empty,
    JsonString,
    JsonOther,
}

/// <summary>A value of a yaml or json tree (FBL §4.1.1): a scalar, a mapping or a sequence, with its span as written.</summary>
internal sealed class TreeValue
{
    public required ValueKind Kind { get; init; }

    public required ValueStyle Style { get; init; }

    /// <summary>The value as written: quotes included; a block collection from its first to its last entry's last byte.</summary>
    public required Span Span { get; set; }

    /// <summary>A scalar's text, decoded.</summary>
    public string Text { get; init; } = "";

    /// <summary>A scalar's value by the YAML 1.2 core schema or JSON's types: string, long, double, bool or null.</summary>
    public object? Typed { get; init; }

    /// <summary>The members of a mapping or the items of a sequence, in order.</summary>
    public List<TreeEntry> Entries { get; } = [];

    /// <summary>A flow collection's members as CEL sees them: readable, but writable only as a whole (FBL §4.3).</summary>
    public object? Flow { get; init; }

    /// <summary>Reached through an alias or a merge key: readable, never writable (FBL §4.3).</summary>
    public bool ViaAlias { get; init; }

    /// <summary>yaml: the mappings a <c>&lt;&lt;</c> key merges into this one, for reading.</summary>
    public List<TreeValue> Merged { get; } = [];

    public TreeEntry? Member(string key) => Entries.FirstOrDefault(e => e.Name == key);
}

/// <summary>A mapping member, a sequence item, or the document root (FBL §4.1.1).</summary>
internal sealed class TreeEntry : Entry
{
    public bool IsRoot { get; init; }

    /// <summary>A member's key as written, quotes included.</summary>
    public Span? KeySpan { get; init; }

    public required TreeValue Value { get; set; }

    /// <summary>json: the offset of the comma after this entry, or -1.</summary>
    public int Separator { get; set; } = -1;

    /// <summary>yaml: the column of the <c>-</c> of an item.</summary>
    public bool IsItem => !IsRoot && KeySpan is null;
}

/// <summary>What yaml and json share: selectors, CEL values and reading keys (FBL §4.2, §4.1.4, §5.2).</summary>
internal abstract class TreeFamily(BodyText text, FblBinding binding, FblOptions options) : FamilyReader(text, binding, options)
{
    protected readonly List<Entry> AllEntries = [];

    public TreeEntry? Root { get; protected set; }

    public override IReadOnlyList<Entry> Entries => AllEntries;

    public override IEnumerable<Candidate> Candidates(Rule rule)
    {
        if (rule.At is null || Root is null) yield break;
        foreach (var (entry, captures) in Selector.Match(Root, rule.At))
        {
            yield return new Candidate(rule, null, entry, captures);
        }
    }

    public override IEnumerable<Entry> Enclosing(Entry entry)
    {
        for (var parent = entry.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TreeEntry { IsRoot: true }) yield break;
            yield return parent;
        }
    }

    public override object? CelValue(Candidate candidate) => Cel(((TreeEntry)candidate.Entry).Value);

    public override (string Name, object? Value) CelExtra(Candidate candidate)
    {
        var path = new CelMap();
        foreach (var (name, value) in candidate.Captures) path[name] = value;
        return ("path", path);
    }

    public static object? Cel(TreeValue value)
    {
        switch (value.Kind)
        {
            case ValueKind.Mapping:
                var map = new CelMap();
                foreach (var merged in value.Merged)
                {
                    if (Cel(merged) is CelMap inherited) foreach (var (k, v) in inherited) map[k] = v;
                }
                foreach (var member in value.Entries)
                {
                    if (member.Name is not null && member.Name != "<<") map[member.Name] = Cel(member.Value);
                }
                return map;
            case ValueKind.Sequence:
                return value.Entries.Select(e => Cel(e.Value)).ToList();
            default:
                return value.Flow ?? value.Typed;
        }
    }

    public override bool HeaderHolds(HeaderSettings header)
    {
        if (header.Key is null) return true;
        if (Root?.Value.Member(header.Key) is not { } member) return false;
        if (header.Value is not { } expected) return true;
        return BindingReader.ScalarText(expected) == member.Value.Text;
    }

    /// <summary>The mapping a slot is read in: the entry's own, or the one <c>child</c> reaches from it.</summary>
    protected TreeValue? Mapping(TreeEntry entry, string? child)
    {
        if (child is null) return entry.Value.Kind == ValueKind.Mapping ? entry.Value : null;
        var reached = Selector.Match(entry, child).FirstOrDefault().Entry as TreeEntry;
        return reached?.Value.Kind == ValueKind.Mapping ? reached.Value : null;
    }

    public override SlotRead Read(Candidate candidate, Slot slot)
    {
        var entry = (TreeEntry)candidate.Entry;
        if (slot.Capture is { } capture)
        {
            if (!candidate.Captures.TryGetValue(capture, out var key)) return SlotRead.Absent;
            for (Entry? e = entry; e is TreeEntry tree; e = e.Parent)
            {
                if (tree.Name == key && tree.KeySpan is { } span) return new SlotRead(key, span, true, true) { Node = tree };
            }
            return new SlotRead(key, null, true, false, "The captured key cannot be found to rewrite.");
        }
        if (slot.Key is not { } name) return SlotRead.ReadOnlyAbsent($"A {FamilyName} entry has no {slot}.");
        var mapping = Mapping(entry, slot.Child);
        if (mapping is null) return SlotRead.Absent;
        return ReadMember(mapping, name);
    }

    protected static SlotRead ReadMember(TreeValue mapping, string name)
    {
        if (mapping.Member(name) is { } member)
        {
            var writable = !member.Value.ViaAlias && member.Value.Kind == ValueKind.Scalar;
            var reason = member.Value.ViaAlias ? "The value is reached through an alias, so it is read-only." : writable ? null : "The value is a block collection, which a slot does not write.";
            return new SlotRead(Cel(member.Value), member.Value.Span, true, writable, reason)
            {
                Node = member,
                Wire = member.Value.Kind == ValueKind.Scalar ? member.Value.Text : null,
            };
        }
        foreach (var merged in mapping.Merged)
        {
            if (merged.Member(name) is { } inherited)
            {
                return new SlotRead(Cel(inherited.Value), inherited.Value.Span, true, false, "The value is reached through a merge key, so it is read-only.") { Node = inherited };
            }
        }
        return SlotRead.Absent with { Node = mapping };
    }

    public override SlotRead ReadRaw(Entry entry, string name) =>
        entry is TreeEntry { Value.Kind: ValueKind.Mapping } tree ? ReadMember(tree.Value, name) : SlotRead.Absent;

    /// <summary>The container entry an insert's <c>container</c> selector names, from the root or the parent entry.</summary>
    protected TreeEntry? Container(string selector, Entry? parent, IReadOnlyDictionary<string, string> captures)
    {
        if (Root is null) return null;
        var resolved = string.Join('/', selector.Split('/').Select(s => s.StartsWith('{') && s.EndsWith('}') && captures.TryGetValue(s[1..^1], out var v) ? v : s));
        if (resolved.StartsWith('/') && resolved.Trim('/').Length == 0) return Root;
        var start = Selector.Start(resolved, Root, parent);
        return Selector.Match(start, resolved).FirstOrDefault().Entry as TreeEntry;
    }

    /// <summary>The captures the binding's existing entries bound, so <c>{capture}</c> segments of a container resolve (FBL §6.2).</summary>
    protected static IReadOnlyDictionary<string, string> CapturesOf(Planning.Plan plan) =>
        plan.Reading.Elements.Select(e => e.Candidate.Captures).FirstOrDefault(c => c.Count > 0) ?? new Dictionary<string, string>();

    protected void Index(TreeEntry entry)
    {
        AllEntries.Add(entry);
        foreach (var child in entry.Value.Entries)
        {
            child.Parent = entry;
            entry.Children.Add(child);
            Index(child);
        }
    }
}
