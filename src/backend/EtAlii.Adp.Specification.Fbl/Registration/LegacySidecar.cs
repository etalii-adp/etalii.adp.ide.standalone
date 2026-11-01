using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Json;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// A sidecar file hosts wrote before FBL (FBL §8.7): a legacy layout, keyed by view key and then by
/// element id with <c>{"x", "y"}</c>, or legacy identities, a natural key mapped to an id. It is read
/// when the registration has no matching block and written back by json splices while it exists;
/// this library never creates one.
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

    /// <summary>Legacy identities: natural key to id.</summary>
    public IReadOnlyDictionary<string, string> Identities()
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        if (IsUnreadable) return identities;
        foreach (var entry in _reading.Root!.Value.Entries)
        {
            if (entry.Name is not null && entry.Value.Typed is string id) identities[entry.Name] = id;
        }
        return identities;
    }

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

    /// <summary>Stores an id for a natural key: replaced in place, or a new member at the end of the object.</summary>
    public PlanResult PlanIdentify(string key, string id)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(id);
        if (IsUnreadable) return new PlanResult.Refused("The identities file could not be read, so it is not written.");
        if (_reading.Root!.Value.Member(key) is { } existing)
        {
            var text = JsonFamily.Quote(id);
            return new PlanResult.Planned(new Edit(_reading.Text.Text(existing.Value.Span) == text ? [] : [new Splice(SpliceOperation.ReplaceValue, existing.Value.Span.Start, existing.Value.Span.End, text)]));
        }
        return Insert(_reading.Root.Value, JsonFamily.Quote(key), _ => JsonFamily.Quote(id));
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
