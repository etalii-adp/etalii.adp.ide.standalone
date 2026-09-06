namespace EtAlii.Adp.Common;

/// <summary>
/// Finds the <see cref="ICommandHandler{TCommand}"/> registered for a command's runtime type
/// and runs it. Dispatching on its own does not record anything - <see cref="IHistoryStack"/>
/// is what turns an execution into an undoable one.
/// </summary>
public interface ICommandDispatcher
{
    /// <exception cref="InvalidOperationException">No handler is registered for the command's type.</exception>
    Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default);
}
