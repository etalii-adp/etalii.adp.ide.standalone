using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The + and − of a node or a flow. The value never goes below zero: a decrement at zero is refused
/// with a sentence rather than written as a negative stock nobody can have.
/// </summary>
public sealed class StepSupplyChainValueCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<StepSupplyChainValueCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(StepSupplyChainValueCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Steps == 0)
        {
            return Task.FromResult(CommandResult.Failure("A step of nothing changes nothing."));
        }

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SupplyChainEdits.NodeOf(model, command.EntryId) is { } node)
            {
                return Stepped(node.Quantity, node.Step, command.Steps, "quantity") is { } quantity
                    ? SupplyChainWriter.SetNumber(document, node.Range, SupplyChainKeys.Quantity, quantity)
                    : AtZero("quantity");
            }

            if (SupplyChainEdits.FlowOf(model, command.EntryId) is { } flow)
            {
                return Stepped(flow.Volume, flow.Step, command.Steps, "volume") is { } volume
                    ? SupplyChainWriter.SetNumber(document, flow.Range, SupplyChainKeys.Volume, volume)
                    : AtZero("volume");
            }

            return SupplyChainEdits.GroupOf(model, command.EntryId) is not null
                ? SupplyChainEdit.Refused("A group has no value of its own; its members do.")
                : SupplyChainEdits.Gone();
        });
    }

    /// <summary>The new value, or <c>null</c> when it is already zero and asked to go lower.</summary>
    internal static double? Stepped(double? current, double? step, int steps, string what)
    {
        _ = what;
        var from = current ?? 0;
        if (from <= 0 && steps < 0)
        {
            return null;
        }

        // Rounded to the precision the writer keeps, so ten steps of 0.1 come back to a whole number.
        return Math.Max(0, Math.Round(from + (steps * (step is > 0 ? step.Value : SupplyChainGeometry.DefaultStep)), 4));
    }

    private static SupplyChainEdit AtZero(string what) => SupplyChainEdit.Refused($"The {what} is already zero.");
}
