using System.Collections.Concurrent;

namespace EtAlii.Adp.History;

/// <summary>
/// Resolves handlers out of the service provider, keyed by the command's runtime type.
/// </summary>
/// <remarks>
/// A command arrives as <see cref="ICommand"/> - the history stack stores it that way - while
/// its handler is the closed generic <c>ICommandHandler&lt;TCommand&gt;</c>. Bridging the two
/// needs one generic step taken at runtime, so each command type gets a small
/// <see cref="ICommandHandlerInvoker"/> built once and cached: the reflection cost is paid on the
/// first dispatch of a type and never again.
/// </remarks>
public sealed class CommandDispatcher : ICommandDispatcher
{
    /// <summary>
    /// Invokers are stateless and depend only on the command type, so one cache serves every
    /// dispatcher instance. The service provider is passed per call rather than captured, which
    /// keeps the cache safe to share across scopes.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ICommandHandlerInvoker> Invokers = new();

    private readonly IServiceProvider _services;

    public CommandDispatcher(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = Invokers.GetOrAdd(command.GetType(), CreateInvoker);
        return invoker.InvokeAsync(_services, command, cancellationToken);
    }

    private static ICommandHandlerInvoker CreateInvoker(Type commandType)
    {
        var invokerType = typeof(CommandHandlerInvoker<>).MakeGenericType(commandType);
        return (ICommandHandlerInvoker)Activator.CreateInstance(invokerType)!;
    }

}
