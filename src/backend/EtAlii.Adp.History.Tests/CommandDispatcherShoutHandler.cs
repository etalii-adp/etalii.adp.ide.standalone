namespace EtAlii.Adp.History.Tests;

internal sealed class CommandDispatcherShoutHandler : ICommandHandler<CommandDispatcherShoutCommand>
{
    public Task<CommandResult> ExecuteAsync(CommandDispatcherShoutCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(CommandResult.Failure($"{command.Name.ToUpperInvariant()}!"));
}
