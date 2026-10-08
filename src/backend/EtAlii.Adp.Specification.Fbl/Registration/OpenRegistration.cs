using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// An open registration (FBL §8): its bytes, its line form, and its own history. Placing an element
/// writes its layout entry by splices, in the ordinal order of ids, with numbers in
/// <c>{decimals: 3}</c> form and the registration's own line ending (FBL §8.3).
/// </summary>
public sealed class OpenRegistration : SplicedFile
{
    private OpenRegistration(byte[] bytes) : base(bytes)
    {
        Document = RegistrationDocument.Read(bytes);
    }

    public RegistrationDocument Document { get; private set; }

    /// <summary>
    /// The ids the reading has, when known. A layout entry for any other id is stale and is removed
    /// as part of the next placement (FBL §8.5).
    /// </summary>
    public IReadOnlySet<string>? KnownIds { get; set; }

    /// <summary>Whether DISL marks an id ephemeral: such an element's position is never stored (FBL §8.5).</summary>
    public Func<string, bool>? IsEphemeral { get; set; }

    /// <summary>The natural keys the reading has, when known: an <c>identities</c> entry for any other key is stale (FBL §8.6).</summary>
    public IReadOnlySet<string>? KnownKeys { get; set; }

    public static OpenRegistration Open(byte[] bytes) => new(bytes ?? throw new ArgumentNullException(nameof(bytes)));

    private PlanResult Plan(ModelChange change)
    {
        switch (change)
        {
            case ModelChange.Save:
                return new PlanResult.Planned(Edit.Empty);
            case ModelChange.Place place:
                if (IsEphemeral?.Invoke(place.Id) == true)
                {
                    return new PlanResult.Refused($"The position of '{place.Id}' is kept for this session only, because its id changes whenever the file is edited.");
                }
                return new PlanResult.Planned(new Edit(PlanPlace(place)));
            case ModelChange.Identify identify:
                return new PlanResult.Planned(new Edit(PlanIdentify(identify)));
            default:
                throw new ArgumentException("A registration is changed only by placing elements and storing ids.", nameof(change));
        }
    }

    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    protected override void Reread() => Document = RegistrationDocument.Read(Bytes);

    private List<Splice> PlanPlace(ModelChange.Place place)
    {
        var x = NewText.Number(place.X, 3);
        var y = NewText.Number(place.Y, 3);
        return PlanEntry(Document.Layout, "layout", Document.AfterHeaders, KnownIds, place.Id, $"{x} {y}", existing =>
        {
            var text = Document.Text;
            var parts = Numbers(text, existing.ValueSpan);
            if (parts.Count != 2) return [new Splice(SpliceOperation.ReplaceValue, existing.ValueSpan.Start, existing.ValueSpan.End, $"{x} {y}")];
            var splices = new List<Splice>();
            if (text.Text(parts[0]) != x) splices.Add(new Splice(SpliceOperation.ReplaceValue, parts[0].Start, parts[0].End, x));
            if (text.Text(parts[1]) != y) splices.Add(new Splice(SpliceOperation.ReplaceValue, parts[1].Start, parts[1].End, y));
            return splices;
        });
    }

    private List<Splice> PlanIdentify(ModelChange.Identify identify)
    {
        var after = Document.Layout is { } layout ? layout.Span.End : Document.AfterHeaders;
        return PlanEntry(Document.Identities, "identities", after, KnownKeys, identify.Key, identify.Id, existing =>
            Document.Text.Text(existing.ValueSpan) == identify.Id ? [] : [new Splice(SpliceOperation.ReplaceValue, existing.ValueSpan.Start, existing.ValueSpan.End, identify.Id)]);
    }

    /// <summary>
    /// Writes one entry of a block (FBL §8.3, §8.6): its value replaced in place when it exists, else
    /// inserted in the ordinal order of keys, creating the block at <paramref name="createAt"/> first;
    /// stale entries (keys not in <paramref name="known"/>) are removed in the same edit (FBL §8.5).
    /// </summary>
    private List<Splice> PlanEntry(RegistrationBlock? block, string name, int createAt, IReadOnlySet<string>? known, string key, string value, Func<RegistrationEntry, List<Splice>> replace)
    {
        var text = Document.Text;
        var splices = new List<Splice>();
        var stale = block is null || known is null ? [] : block.Entries.Where(e => e.Key != key && !known.Contains(e.Key)).ToList();
        foreach (var entry in stale) splices.Add(new Splice(SpliceOperation.RemoveEntry, entry.Line.Start, entry.Line.End, ""));
        var existing = block?.Entries.FirstOrDefault(e => e.Key == key);
        if (existing is not null)
        {
            splices.AddRange(replace(existing));
            return Ordered(splices);
        }
        if (block is null)
        {
            var offset = createAt;
            var newline = text.NewlineAt(Math.Max(0, offset - 1), "\n");
            var lead = offset == text.Length && text.Lines[^1].Ending.Length == 0 && offset > 0 ? newline : "";
            if (lead.Length > 0)
            {
                splices.Add(new Splice(SpliceOperation.EnsureContainer, offset, offset, lead + name + ":"));
                splices.Add(new Splice(SpliceOperation.InsertEntry, offset, offset, $"{newline}  {key}: {value}"));
            }
            else
            {
                splices.Add(new Splice(SpliceOperation.EnsureContainer, offset, offset, name + ":" + newline));
                splices.Add(new Splice(SpliceOperation.InsertEntry, offset, offset, $"  {key}: {value}{newline}"));
            }
            return splices;
        }
        var kept = block.Entries.Except(stale).ToList();
        var next = kept.FirstOrDefault(e => CompareUtf8(e.Key, key) > 0);
        var indent = new string(' ', kept.FirstOrDefault()?.Indent ?? 2);
        var at = next is not null ? next.Line.Start : kept.LastOrDefault()?.Line.End ?? block.NameLine.End;
        var ending = text.NewlineAt(Math.Max(0, at - 1), "\n");
        var entryText = at == text.Length && text.Lines[^1].Ending.Length == 0
            ? $"{ending}{indent}{key}: {value}"
            : $"{indent}{key}: {value}{ending}";
        splices.Add(new Splice(SpliceOperation.InsertEntry, at, at, entryText));
        return Ordered(splices);
    }

    private static List<Splice> Ordered(List<Splice> splices) =>
        splices.Select((s, i) => (s, i)).OrderBy(p => p.s.Start).ThenBy(p => p.i).Select(p => p.s).ToList();

    private static List<Span> Numbers(Text.BodyText text, Span value)
    {
        var spans = new List<Span>();
        var i = value.Start;
        while (i < value.End)
        {
            while (i < value.End && text.Bytes[i] == (byte)' ') i++;
            var start = i;
            while (i < value.End && text.Bytes[i] != (byte)' ') i++;
            if (i > start) spans.Add(new Span(start, i));
        }
        return spans;
    }

    /// <summary>Ordinal order of ids: byte order of their UTF-8 encoding (FBL §8.3).</summary>
    private static int CompareUtf8(string a, string b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));
}
