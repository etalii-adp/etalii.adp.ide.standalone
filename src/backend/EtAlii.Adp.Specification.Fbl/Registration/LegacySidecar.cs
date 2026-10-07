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
public sealed class LegacySidecar : SplicedFile
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

    /// <summary>The positions of one view, its key matched ignoring case (FBL §8.7); without a view, the first.</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Positions(string? view)
    {
        var positions = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        if (View(view) is not { } entry || entry.Value.Kind != ValueKind.Mapping) return positions;
        foreach (var element in entry.Value.Entries)
        {
            if (element.Name is null || element.Value.Kind != ValueKind.Mapping) continue;
            if (Number(element.Value, "x") is { } x && Number(element.Value, "y") is { } y) positions[element.Name] = (x, y);
        }
        return positions;
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

    /// <summary>Places an element in a view: its numbers replaced in place, or a new member at the end of the view's object.</summary>
    public PlanResult PlanPlace(string? view, string id, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (IsUnreadable) return new PlanResult.Refused("The layout file could not be read, so it is not written.");
        var xText = NewText.Number(x, 3);
        var yText = NewText.Number(y, 3);
        var viewEntry = View(view);
        if (viewEntry is null)
        {
            if (view is null) return new PlanResult.Refused("The layout file has no view to place the element in.");
            return Insert(_reading.Root!.Value, Quoted(view), indent => $"{{{Lines(indent, $"{Quoted(id)}: {Position(indent + Step, xText, yText)}")}}}");
        }
        if (viewEntry.Value.Kind != ValueKind.Mapping) return new PlanResult.Refused("The layout file's view is not an object.");
        if (viewEntry.Value.Member(id) is { Value.Kind: ValueKind.Mapping } existing
            && existing.Value.Member("x") is { Value.Kind: ValueKind.Scalar } xMember
            && existing.Value.Member("y") is { Value.Kind: ValueKind.Scalar } yMember)
        {
            var splices = new List<Splice>();
            if (_reading.Text.Text(xMember.Value.Span) != xText) splices.Add(new Splice(SpliceOperation.ReplaceValue, xMember.Value.Span.Start, xMember.Value.Span.End, xText));
            if (_reading.Text.Text(yMember.Value.Span) != yText) splices.Add(new Splice(SpliceOperation.ReplaceValue, yMember.Value.Span.Start, yMember.Value.Span.End, yText));
            return new PlanResult.Planned(new Edit(splices));
        }
        return Insert(viewEntry.Value, Quoted(id), indent => Position(indent, xText, yText));
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
                if (entry.Parent is TreeEntry { IsRoot: false, LineSpan: not null } parent && entry.LineSpan is not null && entry.Indent > parent.Indent)
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
        var last = container.Entries[^1];
        var inserted = last.LineSpan is not null
            ? "," + Newline + new string(' ', last.Indent) + $"{key}: {value(last.Indent)}"
            : $", {key}: {value(last.Indent)}";
        return new PlanResult.Planned(new Edit([new Splice(SpliceOperation.InsertEntry, last.Own.End, last.Own.End, inserted)]));
    }

    private int Column(int offset) => offset - _reading.Text.Lines[_reading.Text.LineIndexAt(offset)].Start;
}
