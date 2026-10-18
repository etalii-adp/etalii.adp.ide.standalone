using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The + and − of a node or a flow. The value never goes below zero: a decrement at zero is refused
/// with a sentence rather than written as a negative stock nobody can have.
/// </summary>
public sealed class StepSupplyChainValueCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<StepSupplyChainValueCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(StepSupplyChainValueCommand command, CancellationToken cancellationToken) =>
        command.Steps == 0 ? CommandResult.Failure("A step of nothing changes nothing.") : await base.RunAsync(command, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, StepSupplyChainValueCommand command)
    {
        if (model.NodeOf(command.EntryId) is { } node)
        {
            return Stepped(node.Quantity, node.Step, command.Steps, "quantity") is { } quantity
                ? SupplyChainWriter.SetNumber(document, node.Range, SupplyChainKeys.Quantity, quantity)
                : AtZero("quantity");
        }

        if (model.FlowOf(command.EntryId) is { } flow)
        {
            return Stepped(flow.Volume, flow.Step, command.Steps, "volume") is { } volume
                ? SupplyChainWriter.SetNumber(document, flow.Range, SupplyChainKeys.Volume, volume)
                : AtZero("volume");
        }

        return model.GroupOf(command.EntryId) is not null
            ? EditOutcome.Refused("A group has no value of its own; its members do.")
            : Gone;
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

    private static EditOutcome AtZero(string what) => EditOutcome.Refused($"The {what} is already zero.");
}
