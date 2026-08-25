using System.Collections.Concurrent;

namespace EtAlii.Adp.Backend;

internal interface ICommandHandlerInvoker
{
    Task<CommandResult> InvokeAsync(IServiceProvider services, ICommand command, CancellationToken cancellationToken);
}
