using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Adds a flow between two nodes, refusing one to itself or one that already exists. Its value is
/// whatever the source has not yet passed on, so a new band continues the quantity rather than
/// inventing one - or 1 when the source has nothing left over.
/// </summary>
public sealed class ConnectSankeyNodesCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<ConnectSankeyNodesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectSankeyNodesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SankeyEdits.NodeOf(model, command.From) is null || SankeyEdits.NodeOf(model, command.To) is null)
            {
                return SankeyEdit.Refused("A flow runs from one node to another.");
            }

            if (command.From == command.To)
            {
                return SankeyEdit.Refused("A node does not flow into itself.");
            }

            if (SankeyEdits.IsTaken(model, SankeyFlow.IdOf(command.From, command.To)) ||
                model.Flows.Any(flow => flow.From == command.From && flow.To == command.To))
            {
                return SankeyEdit.Refused("These two are already joined by a flow in this direction; change its value instead.");
            }

            return SankeyWriter.AddFlow(document, model, new SankeyFlow(
                "", command.From, command.To, Remainder(model, command.From), Step: null, Color: "", Description: "", new LineRange(0, 0)));
        });
    }

    /// <summary>What flows into a node and has not flowed out again, or 1 when nothing is left over.</summary>
    private static double Remainder(SankeyModel model, string id)
    {
        var inflow = model.Flows.Where(flow => flow.To == id).Sum(flow => Math.Max(0, flow.Value ?? 0));
        var outflow = model.Flows.Where(flow => flow.From == id).Sum(flow => Math.Max(0, flow.Value ?? 0));
        var left = Math.Round(inflow - outflow, 4);
        return left > 0 ? left : 1;
    }
}
