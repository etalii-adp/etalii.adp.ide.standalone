using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Json;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// A sidecar file hosts wrote before FBL (FBL §8.7): a legacy layout, keyed by view key and then by
/// element id with <c>{"x", "y"}</c>, or legacy identities, an array of entries <c>{"id", "kind", "key"}</c>
/// (FBL 0.12). It is read when the registration has no matching block and written back by json splices
/// while it exists; this library never creates one.
/// </summary>
internal sealed class LegacySidecar : SplicedFile
{
    private static readonly FblBinding _json = new()
    {
        Name = "sidecar",
        Claims = new Claims(),
        Body = new BodySettings { Family = Family.Json },
    };

    private JsonFamily _reading = null!;

    private LegacySidecar(byte[] bytes) : base(bytes) => Reread();

    /// <summary>Whether the sidecar is not one JSON object; it is then neither applied nor written.</summary>
    public bool IsUnreadable => _reading.Unreadable is not null || _reading.Root?.Value.Kind != ValueKind.Mapping;

    public static LegacySidecar Open(byte[] bytes) => new(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    /// <summary>
    /// The sidecar path a registration's binding names (<c>{base}.layout.json</c> and the like), with
    /// <c>{base}</c> the body's base name, beside the body.
    /// </summary>
    public static string PathFor(string pattern, string bodyPath)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var folder = Path.GetDirectoryName(Path.GetFullPath(bodyPath))!;
        return Path.Combine(folder, pattern.Replace("{base}", Path.GetFileNameWithoutExtension(bodyPath), StringComparison.Ordinal));
    }

    /// <summary>
    /// The positions of one view, its key matched ignoring case (FBL §8.7); without a view, the first. With the reading's
    /// <paramref name="ids"/>, each element key is matched with an id ignoring the case of ASCII letters and the position is
    /// that id's, the first key in the file applying when two match one id (FBL 0.11 §8.7); without them, by the keys as written.
    /// </summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Positions(string? view, IReadOnlySet<string>? ids = null)
    {
        var positions = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        if (View(view) is not { } entry || entry.Value.Kind != ValueKind.Mapping) return positions;
        foreach (var element in entry.Value.Entries)
        {
            if (element.Name is null || element.Value.Kind != ValueKind.Mapping) continue;
            var id = ids is null ? element.Name : IdOf(element.Name, ids);
            if (id is null || positions.ContainsKey(id)) continue;
            if (Number(element.Value, "x") is { } x && Number(element.Value, "y") is { } y) positions[id] = (x, y);
        }
        return positions;
    }

    /// <summary>
    /// What the view's object reports against the reading's <paramref name="ids"/> (FBL 0.11 §8.7): a later key that matches
    /// the id of an earlier one ignoring case, <c>fbl.duplicate-key</c>, neither applied nor written; a key that matches no
    /// id, <c>fbl.stale-view-data</c>, removed at the next write of the view. The other views of the file report nothing.
    /// </summary>
    public IReadOnlyList<Finding> Findings(string? view, IReadOnlySet<string> ids, string fileName)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var findings = new List<Finding>();
        if (View(view) is not { Value.Kind: ValueKind.Mapping } entry) return findings;
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in entry.Value.Entries)
        {
            if (element.Name is not { } key) continue;
            if (IdOf(key, ids) is not { } id)
            {
                findings.Add(Located(FindingCodes.StaleViewData, $"The layout file keeps a position for '{key}', which the file no longer has; it is removed at the next placement in this view.", element, fileName));
            }
            else if (!matched.Add(id))
            {
                findings.Add(Located(FindingCodes.DuplicateKey, $"The layout file keeps a second position for '{id}' under '{key}'; only the first is applied, and this one is not written.", element, fileName));
            }
        }
        return findings;
    }

    /// <summary>
    /// Legacy identities (FBL 0.12 §8.7): each entry's id under its kind and key as the reading looks it up, <c>kind:key</c>,
    /// or the key alone for an entry without a kind; of two entries of one kind and key, the first. A file that is not an
    /// array, as FBL 0.9's object from natural key to id, which no tool wrote, holds none.
    /// </summary>
    public IReadOnlyDictionary<string, string> Identities()
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in IdentityItems())
        {
            if (item is { Id: { } id, Key: { } key }) identities.TryAdd(item.Kind is { } kind ? $"{kind}:{key}" : key, id);
        }
        return identities;
    }

    /// <summary>The entries of a kind no rule of the binding declares, among <paramref name="kinds"/>: each kept as written and reported as <c>fbl.stale-view-data</c> (FBL 0.12 §8.7).</summary>
    public IReadOnlyList<Finding> IdentityFindings(IReadOnlySet<string> kinds, string fileName)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        return [.. IdentityItems()
            .Where(item => item.Kind is { } kind && !kinds.Contains(kind))
            .Select(item => Located(FindingCodes.StaleViewData, $"The identities file keeps an id of the kind '{item.Kind}', which no rule of the binding declares; it is kept as it is.", item.Entry, fileName))];
    }

    /// <summary>
    /// Writes the identities the reading wants (FBL 0.12 §8.7), by json splices: an entry whose id is wanted under another key
    /// has its key replaced; an entry of one of <paramref name="kinds"/> whose id is not wanted is pruned, with the separator
    /// before it, or after it for the first; a wanted id the file does not hold is a new entry after the array's last item,
    /// laid out as that item is, its texts written with RFC 8785's escapes. An entry of any other kind is kept as written.
    /// </summary>
    public PlanResult PlanIdentities(IReadOnlyCollection<LegacyIdentity> wanted, IReadOnlySet<string> kinds)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(kinds);
        if (!HoldsIdentities) return new PlanResult.Refused("The identities file could not be read, so it is not written.");
        var byId = wanted.GroupBy(identity => identity.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var items = IdentityItems();
        var splices = new List<Splice>();
        var pruned = items.Select(item => item is { Id: { } id, Kind: { } kind } && kinds.Contains(kind) && !byId.ContainsKey(id)).ToArray();
        var leading = pruned.TakeWhile(prune => prune).Count();
        for (var i = 0; i < items.Count; i++)
        {
            if (pruned[i])
            {
                splices.Add(Pruning(items, i, leading));
                continue;
            }
            if (items[i] is not { Id: { } id, Key: { } key, KeySpan: { } span } || !byId.TryGetValue(id, out var identity) || identity.Key == key) continue;
            splices.Add(new Splice(SpliceOperation.ReplaceValue, span.Start, span.End, JsonFamily.Quote(identity.Key)));
        }
        var held = items.Select(item => item.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var added = byId.Values.Where(identity => !held.Contains(identity.Id)).ToList();
        if (added.Count > 0) splices.Add(Added(items, added));
        return new PlanResult.Planned(new Edit([.. splices.OrderBy(splice => splice.Start).ThenBy(splice => splice.End)]));
    }

    /// <summary>Whether the file is an array, the form legacy identities are written in (FBL 0.12 §8.7).</summary>
    private bool HoldsIdentities => _reading.Unreadable is null && _reading.Root?.Value.Kind == ValueKind.Sequence;

    /// <summary>The items of a legacy identities file, each with its texts, and the span of its key's value.</summary>
    private List<IdentityItem> IdentityItems()
    {
        if (!HoldsIdentities) return [];
        return [.. _reading.Root!.Value.Entries.Select(entry => new IdentityItem(
            entry,
            entry.Value.Member("id")?.Value.Typed as string,
            entry.Value.Member("kind")?.Value.Typed as string,
            entry.Value.Member("key")?.Value.Typed as string,
            entry.Value.Member("key")?.Value.Span))];
    }

    /// <summary>
    /// The removal of a pruned item with one separator: the one before it, or, for an item of the run of pruned items the
    /// array starts with, the one after it; the last item of an array whose items are all pruned goes alone.
    /// </summary>
    private static Splice Pruning(List<IdentityItem> items, int at, int leading)
    {
        var item = items[at].Entry;
        var span = at >= leading ? item.Own with { Start = items[at - 1].Entry.Own.End }
            : at + 1 < items.Count ? item.Own with { End = items[at + 1].Entry.Own.Start }
            : item.Own;
        return new Splice(SpliceOperation.RemoveEntry, span.Start, span.End, "");
    }

    /// <summary>New items after the array's last, laid out as it is: on a line of its own or after it, its members each on a line of their own or on its brace's; in an empty array, one per line.</summary>
    private Splice Added(List<IdentityItem> items, List<LegacyIdentity> added)
    {
        var root = _reading.Root!.Value;
        if (items.Count == 0)
        {
            var outer = Column(root.Span.Start);
            var text = string.Concat(added.Select((identity, i) => (i > 0 ? "," : "") + Newline + new string(' ', outer + Step) + Item(identity, outer + Step, outer + 2 * Step, true))) + Newline + new string(' ', outer);
            return new Splice(SpliceOperation.InsertEntry, root.Span.Start + 1, root.Span.Start + 1, text);
        }
        var last = items[^1].Entry;
        var indent = Column(last.Own.Start);
        var first = last.Value.Entries.FirstOrDefault();
        var lines = first is not null && _reading.Text.LineIndexAt(first.Own.Start) != _reading.Text.LineIndexAt(last.Own.Start);
        var members = first is null ? indent + Step : Column(first.KeySpan?.Start ?? first.Own.Start);
        var onItsLine = items.Count == 1 ? lines : _reading.Text.LineIndexAt(last.Own.Start) != _reading.Text.LineIndexAt(items[^2].Entry.Own.End);
        var separator = onItsLine ? "," + Newline + new string(' ', indent) : ", ";
        return new Splice(SpliceOperation.InsertEntry, last.Own.End, last.Own.End, string.Concat(added.Select(identity => separator + Item(identity, indent, members, lines))));
    }

    /// <summary>An entry of the identities file: its id, its kind when it has one, and its key, each member on a line of its own or all on the brace's.</summary>
    private string Item(LegacyIdentity identity, int indent, int members, bool lines)
    {
        List<string> parts = [$"\"id\": {JsonFamily.Quote(identity.Id)}", .. identity.Kind is { } kind ? [$"\"kind\": {JsonFamily.Quote(kind)}"] : Array.Empty<string>(), $"\"key\": {JsonFamily.Quote(identity.Key)}"];
        if (!lines) return "{" + string.Join(", ", parts) + "}";
        return "{" + string.Concat(parts.Select((part, i) => (i > 0 ? "," : "") + Newline + new string(' ', members) + part)) + Newline + new string(' ', indent) + "}";
    }

    /// <summary>An item of a legacy identities file, with its texts and the span of its key's value.</summary>
    private sealed record IdentityItem(TreeEntry Entry, string? Id, string? Kind, string? Key, Span? KeySpan);

    /// <summary>
    /// Places an element in a view: its numbers replaced in place, or a new member at the end of the view's object, each
    /// number in its shortest form (FBL 0.11 §8.7). With the reading's <paramref name="ids"/>, the element's member is the
    /// first whose key matches its id ignoring case, and the stale members of the view, whose keys match no id, are removed
    /// in the same edit.
    /// </summary>
    public PlanResult PlanPlace(string? view, string id, double x, double y, IReadOnlySet<string>? ids = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (IsUnreadable) return new PlanResult.Refused("The layout file could not be read, so it is not written.");
        var xText = NewText.Number(x, null);
        var yText = NewText.Number(y, null);
        var viewEntry = View(view);
        if (viewEntry is null)
        {
            if (view is null) return new PlanResult.Refused("The layout file has no view to place the element in.");
            return Insert(_reading.Root!.Value, Quoted(view), indent => $"{{{Lines(indent, $"{Quoted(id)}: {Position(indent + Step, xText, yText)}")}}}");
        }
        if (viewEntry.Value.Kind != ValueKind.Mapping) return new PlanResult.Refused("The layout file's view is not an object.");
        var stale = ids is null ? [] : Pruned(viewEntry.Value, ids);
        var member = viewEntry.Value.Entries.FirstOrDefault(entry => entry.Name is { } key && (ids is null ? key == id : string.Equals(key, id, StringComparison.OrdinalIgnoreCase)));
        if (member is { Value.Kind: ValueKind.Mapping } existing
            && existing.Value.Member("x") is { Value.Kind: ValueKind.Scalar } xMember
            && existing.Value.Member("y") is { Value.Kind: ValueKind.Scalar } yMember)
        {
            var splices = new List<Splice>();
            if (_reading.Text.Text(xMember.Value.Span) != xText) splices.Add(new Splice(SpliceOperation.ReplaceValue, xMember.Value.Span.Start, xMember.Value.Span.End, xText));
            if (_reading.Text.Text(yMember.Value.Span) != yText) splices.Add(new Splice(SpliceOperation.ReplaceValue, yMember.Value.Span.Start, yMember.Value.Span.End, yText));
            splices.AddRange(stale);
            return new PlanResult.Planned(new Edit([.. splices.OrderBy(splice => splice.Start)]));
        }
        if (stale.Count == 0) return Insert(viewEntry.Value, Quoted(id), indent => Position(indent, xText, yText));
        // The new member follows the last member that is kept, before the removals; with none kept it takes the first's place.
        var kept = viewEntry.Value.Entries.Where(entry => entry.Name is { } key && IdOf(key, ids!) is not null).ToList();
        var first = viewEntry.Value.Entries[0];
        var added = kept.Count > 0
            ? Inserted(kept[^1], Quoted(id), indent => Position(indent, xText, yText))
            : new Splice(SpliceOperation.InsertEntry, first.Own.Start, first.Own.Start, $"{Quoted(id)}: {Position(first.Indent, xText, yText)}");
        return new PlanResult.Planned(new Edit([.. stale.Prepend(added).OrderBy(splice => splice.Start).ThenBy(splice => splice.End)]));
    }

    /// <summary>
    /// The removals of the view's stale members (FBL 0.11 §8.7, as §8.5 removes a layout entry): each run of members whose
    /// keys match no id, with one separator, the one after the run, or, for a run that ends the object, the one before it.
    /// </summary>
    private static List<Splice> Pruned(TreeValue view, IReadOnlySet<string> ids)
    {
        var members = view.Entries;
        var splices = new List<Splice>();
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Name is not { } key || IdOf(key, ids) is not null) continue;
            var last = i;
            while (last + 1 < members.Count && members[last + 1].Name is { } next && IdOf(next, ids) is null) last++;
            var span = (i, last == members.Count - 1) switch
            {
                ( > 0, true) => new Span(members[i - 1].Separator, members[last].Own.End),
                (_, false) => new Span(members[i].LineSpan?.Start ?? members[i].Own.Start, members[last + 1].LineSpan?.Start ?? members[last + 1].Own.Start),
                _ => new Span(members[i].Own.Start, members[last].Own.End),
            };
            splices.Add(new Splice(SpliceOperation.RemoveEntry, span.Start, span.End, ""));
            i = last;
        }
        return splices;
    }

    /// <summary>The id a legacy key names, matched ignoring the case of ASCII letters (FBL 0.11 §8.7): the id written as the key, else the first that matches; null for none.</summary>
    private static string? IdOf(string key, IReadOnlySet<string> ids) =>
        ids.Contains(key) ? key : ids.Where(id => string.Equals(id, key, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();

    private Finding Located(string code, string message, TreeEntry member, string fileName)
    {
        var span = member.KeySpan ?? member.Own;
        (int line, int column) = _reading.Text.Position(span.Start);
        return new Finding(code, FindingSeverity.Warning, message, new SourceLocation(fileName, line, column, _reading.Text.CodePoints(span.Start, span.End)));
    }

    public PlanResult Change(Func<LegacySidecar, PlanResult> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = plan(this);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    protected override void Reread()
    {
        _reading = new JsonFamily(new BodyText(Bytes), _json, new FblOptions());
        _reading.Parse();
    }

    private TreeEntry? View(string? view)
    {
        if (IsUnreadable) return null;
        var members = _reading.Root!.Value.Entries;
        return view is null ? members.FirstOrDefault() : members.FirstOrDefault(m => string.Equals(m.Name, view, StringComparison.OrdinalIgnoreCase));
    }

    private static double? Number(TreeValue mapping, string key) =>
        mapping.Member(key)?.Value.Typed switch
        {
            long l => l,
            double d => d,
            _ => null,
        };

    private static string Quoted(string value) => JsonFamily.Quote(value);

    /// <summary>The indentation step of the file: a member's indentation minus its object's, else two.</summary>
    private int Step
    {
        get
        {
            foreach (var entry in _reading.Entries.OfType<TreeEntry>())
            {
                if (entry is { Parent: TreeEntry { IsRoot: false, LineSpan: not null } parent, LineSpan: not null } && entry.Indent > parent.Indent)
                {
                    return entry.Indent - parent.Indent;
                }
            }
            return 2;
        }
    }

    private string Newline => _reading.Text.DominantEnding ?? "\n";

    private string Lines(int indent, string member) => Newline + new string(' ', indent + Step) + member + Newline + new string(' ', indent);

    private string Position(int indent, string x, string y) =>
        "{" + Newline + new string(' ', indent + Step) + $"\"x\": {x}," + Newline + new string(' ', indent + Step) + $"\"y\": {y}" + Newline + new string(' ', indent) + "}";

    /// <summary>A new member after the object's last, written as the file writes its members (FBL §6.3, json values).</summary>
    private PlanResult Insert(TreeValue container, string key, Func<int, string> value)
    {
        if (container.Entries.Count == 0)
        {
            var indent = Column(container.Span.Start) + Step;
            var text = Newline + new string(' ', indent) + $"{key}: {value(indent)}" + Newline + new string(' ', Column(container.Span.Start));
            return new PlanResult.Planned(new Edit([new Splice(SpliceOperation.InsertEntry, container.Span.Start + 1, container.Span.Start + 1, text)]));
        }
        return new PlanResult.Planned(new Edit([Inserted(container.Entries[^1], key, value)]));
    }

    /// <summary>A new member right after <paramref name="last"/>, laid out as it is: on its own line, or on the line it shares.</summary>
    private Splice Inserted(TreeEntry last, string key, Func<int, string> value)
    {
        var inserted = last.LineSpan is not null
            ? "," + Newline + new string(' ', last.Indent) + $"{key}: {value(last.Indent)}"
            : $", {key}: {value(last.Indent)}";
        return new Splice(SpliceOperation.InsertEntry, last.Own.End, last.Own.End, inserted);
    }

    private int Column(int offset) => offset - _reading.Text.Lines[_reading.Text.LineIndexAt(offset)].Start;
}
