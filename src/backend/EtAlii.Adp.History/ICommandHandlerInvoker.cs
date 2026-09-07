namespace EtAlii.Adp.History;

internal interface ICommandHandlerInvoker
{
    Task<CommandResult> InvokeAsync(IServiceProvider services, ICommand command, CancellationToken cancellationToken);
}
