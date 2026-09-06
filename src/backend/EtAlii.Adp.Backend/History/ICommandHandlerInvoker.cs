using EtAlii.Adp.Common;
namespace EtAlii.Adp.Backend;

internal interface ICommandHandlerInvoker
{
    Task<CommandResult> InvokeAsync(IServiceProvider services, ICommand command, CancellationToken cancellationToken);
}
