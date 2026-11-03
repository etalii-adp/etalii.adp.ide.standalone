using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Specification.Fbl.History;

/// <summary>
/// An open body whose rules keep their ids beside it, in the legacy identities file (FBL 0.12 §8.6, §8.7), with that
/// file: every edit of the body writes the entries the edit changes in the same edit, and an undo or a redo takes both
/// files back together.
/// </summary>
/// <remarks>
/// <para>
/// <b>An entry moves with its key.</b> A key is computed from values of the body (a component's name, a link's two ends),
/// so an edit that changes those values changes the key, and the reading after it no longer finds the element's id under
/// it. Each element read after the edit is matched with the one it was before by the place of its entry: the entry's
/// start, moved by the bytes the edit's splices add or remove before it, is where the same entry starts now, unless a
/// splice that writes no text took the whole entry away. A matched element keeps its id, and its entry's key is replaced;
/// an element the edit took away has its entry pruned; an element no entry was before is the edit's own, and its entry is
/// added, with the id the add gives (<see cref="ModelChange.Add.Id"/>) when it gives one. Those entries are steps of the
/// edit's history.
/// </para>
/// <para>
/// <b>An id first stored is no step of the history.</b> An element that was read without an entry, and that the edit
/// leaves where it was, has the id the reading gave it written with the edit, outside the file's history (FBL 0.12 §8.6),
/// so that undoing the edit does not take away the id the element is known by.
/// </para>
/// <para>
/// A host that keeps an edit's undo its own, by restoring the whole text, uses this for the ids an edit carries, and writes
/// the identities where its document keeps them.
/// </para>
/// </remarks>
public sealed class IdentifiedBody
{
    /// <summary>For each edit that can be undone, and each that can be redone, whether it wrote the identities file.</summary>
    private readonly Stack<bool> _done = new();
    private readonly Stack<bool> _undone = new();

    /// <summary>The <c>sidecar.kind</c> of every rule of the binding: the entries of any other kind are kept as written.</summary>
    private readonly IReadOnlySet<string> _kinds;

    private IdentifiedBody(OpenBody body, LegacySidecar identities, IReadOnlySet<string> kinds)
    {
        Body = body;
        Identities = identities;
        _kinds = kinds;
    }

    /// <summary>The body, read with the ids the identities file keeps.</summary>
    public OpenBody Body { get; }

    /// <summary>The legacy identities file the body's sidecar ids are kept in.</summary>
    public LegacySidecar Identities { get; }

    /// <summary>
    /// Opens <paramref name="body"/> through <paramref name="binding"/> with the ids <paramref name="identities"/> keeps,
    /// the options' own <see cref="FblOptions.Identities"/> replaced by them.
    /// </summary>
    public static IdentifiedBody Open(byte[] body, FblBinding binding, FblOptions options, LegacySidecar identities)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identities);
        var kinds = binding.AllRules.Select(rule => rule.Id?.SidecarKind).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new IdentifiedBody(OpenBody.Open(body, binding, options.With(options.Origin, options.Resource, identities.Identities())), identities, kinds);
    }

    /// <summary>
    /// Plans and applies <paramref name="change"/> to the body, and writes the identities file's entries it changes in the
    /// same edit: the body's result, and the splices of the identities file that are a step of its history.
    /// </summary>
    public IdentifiedEdit Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Body.Model;
        var result = Body.Change(change);
        if (result is not PlanResult.Planned { Edit: { Splices.Count: > 0 } edit }) return new IdentifiedEdit(result, SpliceEdit.Empty);

        (List<LegacyIdentity> wanted, List<LegacyIdentity> firstStored) = Wanted(before, Body.Model, edit.Splices, change as ModelChange.Add);
        var planned = Identities.PlanIdentities(wanted, _kinds);
        var written = SpliceEdit.Empty;
        if (planned is PlanResult.Planned { Edit: var identities })
        {
            Identities.Apply(identities);
            written = identities;
            if (firstStored.Count > 0 && Identities.PlanIdentities([.. wanted, .. firstStored], _kinds) is PlanResult.Planned { Edit: var stored })
            {
                Identities.StoreOutsideHistory(stored);
            }
        }
        Body.Identify(Identities.Identities());
        _done.Push(written.Splices.Count > 0);
        _undone.Clear();
        return new IdentifiedEdit(result, written);
    }

    /// <summary>Undoes the most recent edit of the body, and of the identities file when it wrote that too: the splices of each.</summary>
    internal (UndoResult Body, IReadOnlyList<Splice> Identities) Undo()
    {
        var undone = Body.Undo();
        if (undone is not UndoResult.Done) return (undone, []);
        var wrote = _done.Count > 0 && _done.Pop();
        _undone.Push(wrote);
        return (undone, Reidentified(wrote ? Identities.Undo() : null));
    }

    /// <summary>Redoes the most recently undone edit of the body, and of the identities file when it wrote that too.</summary>
    internal (UndoResult Body, IReadOnlyList<Splice> Identities) Redo()
    {
        var redone = Body.Redo();
        if (redone is not UndoResult.Done) return (redone, []);
        var wrote = _undone.Count > 0 && _undone.Pop();
        _done.Push(wrote);
        return (redone, Reidentified(wrote ? Identities.Redo() : null));
    }

    /// <summary>The identities file's splices of an undo or a redo, after the body is read again with the ids the file keeps now.</summary>
    private List<Splice> Reidentified(UndoResult? identities)
    {
        Body.Identify(Identities.Identities());
        return identities is UndoResult.Done done ? [.. done.Splices] : [];
    }

    /// <summary>
    /// The entries the reading <paramref name="after"/> the edit wants as steps of the edit, and those it stores for the
    /// first time: each element of a rule with a sidecar id under its key, with the id of the element it was
    /// <paramref name="before"/> the edit when it is matched with one (see the remarks of <see cref="IdentifiedBody"/>).
    /// </summary>
    private static (List<LegacyIdentity> Wanted, List<LegacyIdentity> FirstStored) Wanted(FblModel before, FblModel after, IReadOnlyList<Splice> splices, ModelChange.Add? add)
    {
        var kept = before.Elements.Where(element => element.IdentityKey is not null && !TakenAway(element.OwnSpan, splices)).ToList();
        var wanted = new List<LegacyIdentity>();
        var firstStored = new List<LegacyIdentity>();
        var matched = new HashSet<FblElement>();
        var added = add?.Id;
        foreach (var element in after.Elements)
        {
            if (element.IdentityKey is not { } key) continue;
            var earlier = kept.FirstOrDefault(old => !matched.Contains(old) && old.Type == element.Type && Moved(old.OwnSpan.Start, splices) == element.OwnSpan.Start);
            if (earlier is not null)
            {
                matched.Add(earlier);
                (earlier.IdIsStored ? wanted : firstStored).Add(new LegacyIdentity(earlier.Id, element.IdentityKind, key));
                continue;
            }
            var id = added is not null && element.Type == add!.Type ? added : element.Id;
            if (id == added) added = null;
            wanted.Add(new LegacyIdentity(id, element.IdentityKind, key));
        }
        return (wanted, firstStored);
    }

    /// <summary>Where the byte at <paramref name="offset"/> is after <paramref name="splices"/>: moved by every splice that ends at or before it.</summary>
    private static int Moved(int offset, IReadOnlyList<Splice> splices)
    {
        var moved = offset;
        foreach (var splice in splices)
        {
            if (splice.End <= offset) moved += System.Text.Encoding.UTF8.GetByteCount(splice.Text) - (splice.End - splice.Start);
        }
        return moved;
    }

    /// <summary>Whether a splice that writes no text covers the whole of an entry: the entry is then taken away.</summary>
    private static bool TakenAway(Span entry, IReadOnlyList<Splice> splices) =>
        splices.Any(splice => splice.Text.Length == 0 && splice.Start <= entry.Start && splice.End >= entry.End && splice.End > splice.Start);
}

/// <summary>
/// What an edit of an <see cref="IdentifiedBody"/> did: the body's result, and the splices of the identities file that are a
/// step of its history, empty when it wrote none.
/// </summary>
public sealed record IdentifiedEdit(PlanResult Body, SpliceEdit Identities);
