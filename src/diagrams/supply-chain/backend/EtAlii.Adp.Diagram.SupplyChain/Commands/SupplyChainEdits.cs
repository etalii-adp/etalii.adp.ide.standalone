using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Writes what a transaction of the definition changes (DISL §9.4) into a <see cref="SupplyChainBody"/>: every change
/// as the FBL model change that writes it (<see cref="DislWrite.ToFbl"/>), which the library plans as splices of the
/// body (supply-chain-disl-fbl Requirement 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here builds a line of YAML.</b> Where a key goes, how a value is quoted, what indentation and line ending a
/// new entry gets and what happens to the comments around an entry all follow the binding and the body's own
/// conventions (FBL section 6.3), not code in this module (Q1).
/// </para>
/// <para>
/// <b>An element is written by the id the binding reads it with</b> (<see cref="SupplyChainBody.BindingEntries"/>), which
/// for a later holder of an id and an entry without one is its place, read once from the body as it was, and a
/// reference - a node's group, a flow's ends - is written as the id its element is written with.
/// </para>
/// <para>
/// <b>The entries are edited bottom-up, then the new ones appended</b>, each change of one entry in the transaction's
/// order. A place is a line, so editing bottom-up leaves every place above where it was read; it is how a node is
/// removed with its flows and a group from its members. Changes that each set one entry, as placing and arranging the
/// nodes do, are planned against the body as it was and written as one edit (<see cref="SupplyChainBody.Batch"/>).
/// </para>
/// <para>
/// <b>How a value is written is the binding's</b>: a position is rounded to whole units and an amount to four decimals,
/// halves away from zero, and a blank text or an unset number removes its key (FBL section 6.3).
/// </para>
/// <para>
/// <b>A change the body cannot write refuses the edit</b> with the library's sentence, never dropped silently.
/// </para>
/// </remarks>
internal static class SupplyChainEdits
{
    /// <summary>Writes <paramref name="transaction"/>, run over <paramref name="before"/>, the DISL model of <paramref name="body"/>, into it; or its refusal.</summary>
    public static EditOutcome Apply(SupplyChainBody body, DislDiagram before, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied)
        {
            return EditOutcome.Refused(transaction.Refusal!);
        }

        var created = transaction.Changes.Where(change => change is DislChange.Create or DislChange.Connect).ToList();
        var edits = transaction.Changes.Except(created).ToList();
        if (edits.FirstOrDefault(change => ElementOf(before, change) is null) is { } unwritable)
        {
            return EditOutcome.Refused($"The definition makes a change this diagram cannot write: {unwritable.GetType().Name.ToLowerInvariant()}.");
        }

        var entries = body.BindingEntries;
        var ids = entries.ToDictionary(entry => entry.Key, entry => entry.Value.Id, StringComparer.Ordinal);
        var ordered = Uncascaded(body, entries, ids, before, edits)
            .GroupBy(change => ElementOf(before, change)!)
            .OrderByDescending(entry => entry.Key.Line)
            .SelectMany(entry => entry)
            .Concat(created)
            .ToList();
        return created.Count == 0 && ordered.All(change => change is DislChange.Set) && ordered.Cast<DislChange.Set>().DistinctBy(set => set.ElementId).Count() == ordered.Count
            ? Together(body, ordered.Select(change => ForBinding(ids, before, change)).ToList())
            : OneByOne(body, ordered.Select(change => ForBinding(ids, before, change)).ToList());
    }

    /// <summary>Writes <paramref name="changes"/> one at a time, each into the body the one before left; the first refusal stops it.</summary>
    private static EditOutcome OneByOne(SupplyChainBody body, List<ModelChange> changes)
    {
        foreach (var change in changes)
        {
            var outcome = body.Change(change);
            if (!outcome.WasApplied)
            {
                return outcome;
            }
        }

        return EditOutcome.Applied;
    }

    /// <summary>Writes <paramref name="changes"/>, each of its own entry, as one edit planned against the body as it was; the first refusal refuses it.</summary>
    private static EditOutcome Together(SupplyChainBody body, List<ModelChange> changes)
    {
        EditOutcome? refused = null;
        body.Batch(() =>
        {
            foreach (var change in changes)
            {
                var outcome = body.Change(change);
                refused ??= outcome.WasApplied ? null : outcome;
            }
        });
        return refused ?? EditOutcome.Applied;
    }

    /// <summary>
    /// <paramref name="changes"/> without the removal of an entry that removing another of them removes with it: the
    /// binding removes a node's flows with the node (its <c>cascade</c>), so the flow's own removal would find it gone or,
    /// by the id it was written with, find another entry that now holds that id. Which flows go with a node is the
    /// library's to say, so it is asked: the node's removal is planned against the body as it is, and a flow whose entry
    /// lies inside what that removes is left to it.
    /// </summary>
    private static List<DislChange> Uncascaded(SupplyChainBody body, IReadOnlyDictionary<string, FblElement> entries, Dictionary<string, string> ids, DislDiagram before, List<DislChange> changes)
    {
        List<Span> removed =
        [
            .. changes.OfType<DislChange.Remove>()
                .Where(remove => before.ElementById(remove.ElementId) is { Type.IsRelation: false })
                .SelectMany(remove => body.Planned(ForBinding(ids, before, remove))),
        ];
        return
        [
            .. changes.Where(change => change is not DislChange.Remove remove
                || before.ElementById(remove.ElementId) is not { Type.IsRelation: true }
                || !entries.TryGetValue(remove.ElementId, out var entry)
                || !removed.Any(span => span.Start <= entry.OwnSpan.Start && entry.OwnSpan.End <= span.End)),
        ];
    }

    /// <summary>The element of <paramref name="before"/> a change of an existing entry changes; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram before, DislChange change) => change switch
    {
        DislChange.Set set => before.ElementById(set.ElementId),
        DislChange.Remove remove => before.ElementById(remove.ElementId),
        _ => null,
    };

    /// <summary><paramref name="change"/> as the change the binding writes: every element by the id the binding reads it with, every reference by the id it is written with.</summary>
    private static ModelChange ForBinding(Dictionary<string, string> ids, DislDiagram before, DislChange change) => DislWrite.ToFbl(SupplyChainDefinition.Specification, change) switch
    {
        ModelChange.Set set => set with { Id = ids.GetValueOrDefault(set.Id, set.Id), Attributes = Written(before, set.Attributes) },
        ModelChange.Remove remove => new ModelChange.Remove(ids.GetValueOrDefault(remove.Id, remove.Id)),
        ModelChange.Add add => add with { Attributes = Written(before, add.Attributes) },
        var other => other,
    };

    /// <summary><paramref name="attributes"/> with a node's group and a flow's ends as the ids their elements are written with.</summary>
    private static Dictionary<string, object?> Written(DislDiagram before, IReadOnlyDictionary<string, object?> attributes) =>
        attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Key is "group" or "from" or "to" && attribute.Value is string id && before.ElementById(id) is { } element
                ? SupplyChainWritten.IdOf(element)
                : attribute.Value,
            StringComparer.Ordinal);
}
