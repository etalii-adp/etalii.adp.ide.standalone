using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Arranges a diagram by moving node entries among the others, the way a drag up or down a column
/// does - one entry at a time, each move re-read, so every move splices lines the parser just found.
/// </summary>
public sealed class ArrangeSankeyCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<ArrangeSankeyCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SankeyModel model, ArrangeSankeyCommand command)
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
            var parsed = SankeyParser.Parse(document);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var current = parsed.Nodes.Where(node => wanted.Contains(node.Id) && taken.Add(node.Id)).ToList();
            if (current[index].Id == target[index])
            {
                continue;
            }

            SankeyWriter.MoveNode(document, parsed.NodeOf(target[index])!, current[index], after: false);
            moved = true;
        }

        return moved ? EditOutcome.Applied : EditOutcome.Refused("This diagram is already arranged.");
    }
}
