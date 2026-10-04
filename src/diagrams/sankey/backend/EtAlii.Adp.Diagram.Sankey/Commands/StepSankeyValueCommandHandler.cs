using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The + and − of a flow. The value never goes below zero: a decrement at zero is refused with a
/// sentence rather than written as a negative quantity nothing can carry.
/// </summary>
public sealed class StepSankeyValueCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<StepSankeyValueCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(StepSankeyValueCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Steps == 0)
        {
            return Task.FromResult(CommandResult.Failure("A step of nothing changes nothing."));
        }

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SankeyEdits.FlowOf(model, command.FlowId) is not { } flow)
            {
                return SankeyEdits.NodeOf(model, command.FlowId) is not null
                    ? SankeyEdit.Refused("A node's value is what flows through it; change a flow's value instead.")
                    : SankeyEdits.Gone();
            }

            return Stepped(flow.Value, flow.Step, command.Steps) is { } value
                ? SankeyWriter.SetNumber(document, flow.Range, SankeyKeys.Value, value)
                : SankeyEdit.Refused("The value is already zero.");
        });
    }

    /// <summary>
    /// What one step adds to a flow that states none: a tenth of the value's own order of magnitude,
    /// so a flow of 3.3 steps by 0.1 and one of 1,302 by 100 - the same few presses either way.
    /// </summary>
    public static double DefaultStep(double? value)
    {
        var magnitude = Math.Abs(value ?? 0);
        return magnitude < 1 ? 0.1 : Math.Pow(10, Math.Floor(Math.Log10(magnitude)) - 1) is var step && step >= 0.1 ? step : 0.1;
    }

    /// <summary>The new value, or <c>null</c> when it is already zero and asked to go lower.</summary>
    internal static double? Stepped(double? current, double? step, int steps)
    {
        var from = Math.Max(0, current ?? 0);
        if (from <= 0 && steps < 0)
        {
            return null;
        }

        // Rounded to the precision the writer keeps, so ten steps of 0.1 come back to a whole number.
        return Math.Max(0, Math.Round(from + (steps * (step is > 0 ? step.Value : DefaultStep(current))), 4));
    }
}
