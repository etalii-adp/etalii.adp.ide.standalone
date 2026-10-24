using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Arranges a diagram by moving node entries among the others, the way a drag up or down a column
/// does: one <c>ModelChange.Move</c> at a time, each planned on the body the last
/// one left, so every entry keeps its own bytes. It is one command, so one edit and one undo step.
/// </summary>
/// <remarks>
/// The moves are not one batch (<c>FblBodyEdits.Batch</c>): a batch plans every change against the body as it was,
/// and two moves of a reordering often take their place beside the same entry, which only applying them in turn can
/// state.
/// </remarks>
public sealed class ArrangeSankeyCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<ArrangeSankeyCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SankeyBody document, SankeyModel model, ArrangeSankeyCommand command)
    {
        var target = SankeyArrangement.DocumentOrderOf(model);
        if (target.Count == 0)
        {
            return EditOutcome.Refused("There is nothing to arrange until this diagram has a node.");
        }

        var wanted = target.ToHashSet(StringComparer.Ordinal);
        var moved = false;
        for (var index = 0; index < target.Count; index++)
        {
            var parsed = document.Reading.Model;
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var current = parsed.Nodes.Where(node => wanted.Contains(node.Id) && taken.Add(node.Id)).ToList();
            if (current[index].Id == target[index])
            {
                continue;
            }

            if (SankeyEdits.MoveNode(document, parsed.NodeOf(target[index])!, current[index], after: false) is { WasApplied: false } refused)
            {
                return refused;
            }

            moved = true;
        }

        return moved ? EditOutcome.Applied : EditOutcome.Refused("This diagram is already arranged.");
    }
}
