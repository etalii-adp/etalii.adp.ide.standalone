using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Several commands as one step of the history: run in order, undone together in the reverse order. A renamed task's
/// stored position is moved this way, beside the rename of its key in the body (finding B16, Requirement 6.6), so that
/// one undo restores the body and the registration together.
/// </summary>
/// <param name="Steps">The commands, in the order they run.</param>
public sealed record DatabricksStepsCommand(IReadOnlyList<ICommand> Steps) : ICommand;

/// <inheritdoc cref="DatabricksStepsCommand" />
/// <remarks>
/// A step that fails undoes the steps before it, so a refusal leaves both files as they were; its inverse is the inverses
/// of the steps, latest first, so its redo is the steps again.
/// </remarks>
public sealed class DatabricksStepsCommandHandler(ICommandDispatcher dispatcher) : ICommandHandler<DatabricksStepsCommand>
{
    private readonly ICommandDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(DatabricksStepsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var inverses = new List<ICommand>();
        var warnings = new List<string>();
        foreach (var step in command.Steps)
        {
            var result = await _dispatcher.DispatchAsync(step, cancellationToken);
            if (!result.IsSuccess)
            {
                await UndoAsync(inverses);
                return result;
            }

            if (result.Inverse is { } inverse)
            {
                inverses.Insert(0, inverse);
            }

            if (result.Warning.Length > 0)
            {
                warnings.Add(result.Warning);
            }
        }

        if (inverses.Count == 0)
        {
            return CommandResult.Success();
        }

        var undo = new DatabricksStepsCommand(inverses);
        return warnings.Count > 0 ? CommandResult.Success(undo, string.Join(" ", warnings)) : CommandResult.Success(undo);
    }

    /// <summary>Undoes the steps that ran, latest first, whatever the step that failed was asked to stop.</summary>
    private async Task UndoAsync(List<ICommand> inverses)
    {
        foreach (var inverse in inverses)
        {
            await _dispatcher.DispatchAsync(inverse, CancellationToken.None);
        }
    }
}
