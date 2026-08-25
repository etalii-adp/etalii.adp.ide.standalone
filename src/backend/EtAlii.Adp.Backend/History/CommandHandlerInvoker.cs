namespace EtAlii.Adp.Backend;

internal sealed class CommandHandlerInvoker<TCommand> : ICommandHandlerInvoker
    where TCommand : ICommand
{
    public Task<CommandResult> InvokeAsync(
        IServiceProvider services,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(ICommandHandler<TCommand>)) is not ICommandHandler<TCommand> handler)
        {
            // A missing handler is a wiring mistake, not a rejected command, so it throws
            // rather than returning a failure the caller might show to a user.
            throw new InvalidOperationException(
                $"No command handler is registered for '{typeof(TCommand).FullName}'.");
        }

        return handler.ExecuteAsync((TCommand)command, cancellationToken);
    }
}
