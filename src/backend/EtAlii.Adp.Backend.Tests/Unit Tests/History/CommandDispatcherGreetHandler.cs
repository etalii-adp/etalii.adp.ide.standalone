using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class CommandDispatcherGreetHandler : ICommandHandler<CommandDispatcherGreetCommand>
{
    public CommandDispatcherGreetCommand? Received { get; private set; }

    public CancellationToken ReceivedToken { get; private set; }

    public int CallCount { get; private set; }

    public Task<CommandResult> ExecuteAsync(CommandDispatcherGreetCommand command, CancellationToken cancellationToken = default)
    {
        Received = command;
        ReceivedToken = cancellationToken;
        CallCount++;
        return Task.FromResult(CommandResult.Success(new CommandDispatcherGreetCommand($"un-{command.Name}")));
    }
}
