using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// An open registration (FBL §8): its bytes, its line form, and its own history. Placing an element
/// writes its layout entry by splices, in the ordinal order of ids, with numbers in
/// <c>{decimals: 3}</c> form and the registration's own line ending (FBL §8.3).
/// </summary>
/// <remarks>
/// Public because another project uses it (T226's rule): <c>EtAlii.Adp.Hierarchy</c>'s
/// <c>PlaceInRegistrationCommand</c> writes a moved element's position through it. The document it
/// reads stays internal; a caller places and takes the bytes.
/// </remarks>
public sealed class OpenRegistration : SplicedFile
{
    /// <summary>The blocks the binding declares (FBL 0.7 §8.1), in the order it declares them.</summary>
    private readonly IReadOnlyList<RegistrationBlockDeclaration> _declarations;

    /// <summary>The headers the binding declares (FBL §8.1).</summary>
    private readonly IReadOnlyList<string> _headers;

    private OpenRegistration(byte[] bytes, RegistrationSettings settings) : base(bytes)
    {
        _declarations = settings.Blocks;
        _headers = settings.Headers;
        Document = RegistrationDocument.Read(bytes, Declared);
    }

    private string[] Declared => [.. _declarations.Select(declaration => declaration.Name)];

    internal RegistrationDocument Document { get; private set; }

    /// <summary>
    /// The ids the reading has, when known. A layout entry for any other id is stale and is removed
    /// as part of the next placement (FBL §8.5).
    /// </summary>
    public IReadOnlySet<string>? KnownIds { get; set; }

    /// <summary>Whether DISL marks an id ephemeral: such an element's position is never stored (FBL §8.5).</summary>
    public Func<string, bool>? IsEphemeral { get; set; }

    /// <summary>
    /// The members a declared block stands for while it is absent, by its name, in their order: the initial and the
    /// implicit members of its list, which the specification gives and the body implies (DESL 0.2 §5.5, §7.7.2). The
    /// first change to such a block writes them all, with the change applied (FBL 0.7 §8.4); null, or a block it
    /// gives nothing for, writes the changed entry alone.
    /// </summary>
    public Func<string, IReadOnlyList<KeyValuePair<string, string>>?>? AbsentMembers { get; set; }

    /// <summary>
    /// The entries of the declared block <paramref name="block"/> (FBL 0.7 §8.1), by key and value in their order, or null
    /// when the registration does not hold it.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>>? BlockEntries(string block) =>
        Document.Block(block)?.Entries.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value)).ToList();

    /// <summary>The natural keys the reading has, when known: an <c>identities</c> entry for any other key is stale (FBL §8.6).</summary>
    public IReadOnlySet<string>? KnownKeys { get; set; }

    public static OpenRegistration Open(byte[] bytes) => new(bytes ?? throw new ArgumentNullException(nameof(bytes)), new RegistrationSettings());

    /// <summary>Opens a registration through its binding, which declares its headers and blocks (FBL 0.7 §8.1).</summary>
    public static OpenRegistration Open(byte[] bytes, FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(bytes ?? throw new ArgumentNullException(nameof(bytes)), binding.Registration);
    }

    /// <summary>
    /// What reading the registration reports (FBL §8.1, §8.5, 0.7 §8.1): the headers and blocks neither FBL nor the
    /// binding declares, and, when <see cref="KnownIds"/> is set, the entries for ids the body does not have.
    /// </summary>
    public IReadOnlyList<Finding> Findings(string fileName)
    {
        var findings = new List<Finding>();
        findings.AddRange(Document.UnknownHeaders(_headers, fileName));
        findings.AddRange(Document.UnknownBlocks(fileName));
        if (KnownIds is { } ids) findings.AddRange(Document.StaleEntries(ids, fileName, _declarations));
        return findings;
    }

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
            case ModelChange.Setting setting:
                if (Declaration(setting.Block) is not { } declaration)
                {
                    return new PlanResult.Refused($"The binding declares no block '{setting.Block}'.");
                }
                try
                {
                    return new PlanResult.Planned(new Edit(DeclaredBlockWriter.Plan(Document, _declarations, declaration, setting, KnownIds, AbsentMembers?.Invoke(setting.Block))));
                }
                catch (RefusedException refused)
                {
                    return new PlanResult.Refused(refused.Message);
                }
            default:
                throw new ArgumentException("A registration is changed only by placing elements, storing ids and writing settings.", nameof(change));
        }
    }

    /// <summary>
    /// Plans and applies <paramref name="change"/>. A setting of a block declared <c>history: false</c> is applied
    /// outside the history (FBL 0.7 §7.1): no undo step, and the history carried over it.
    /// </summary>
    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is not PlanResult.Planned planned) return result;
        if (change is ModelChange.Setting setting && Declaration(setting.Block) is { InHistory: false }) ApplyOutsideHistory(planned.Edit);
        else Apply(planned.Edit);
        return result;
    }

    private RegistrationBlockDeclaration? Declaration(string name) => _declarations.FirstOrDefault(declaration => declaration.Name == name);

    protected override void Reread() => Document = RegistrationDocument.Read(Bytes, Declared);

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

    /// <summary>
    /// The splices in body order. At one offset an insertion goes before a removal that starts there,
    /// as when a new entry follows the last kept one and a stale entry comes next (FBL §8.5, §8.6).
    /// </summary>
    private static List<Splice> Ordered(List<Splice> splices) =>
        splices.Select((s, i) => (s, i)).OrderBy(p => p.s.Start).ThenBy(p => p.s.End).ThenBy(p => p.i).Select(p => p.s).ToList();

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
