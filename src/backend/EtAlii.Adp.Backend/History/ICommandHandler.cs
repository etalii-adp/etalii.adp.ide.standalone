namespace EtAlii.Adp.Backend.History;

/// <summary>
/// Carries out one specific <see cref="ICommand"/>. Exactly one handler is registered per
/// command type; <see cref="ICommandDispatcher"/> is what finds it.
/// </summary>
/// <remarks>
/// A handler that changes state should report an <see cref="CommandResult.Inverse"/> - the
/// command that puts things back - so the change can be undone. A handler that changes
/// nothing (or whose change is deliberately not undoable) reports plain
/// <see cref="CommandResult.Success()"/> and is simply not recorded.
/// <para>
/// Handlers must be re-runnable: undo and redo dispatch them again with the recorded
/// arguments, so each one validates its own preconditions rather than trusting a caller
/// to have done so earlier.
/// </para>
/// </remarks>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
