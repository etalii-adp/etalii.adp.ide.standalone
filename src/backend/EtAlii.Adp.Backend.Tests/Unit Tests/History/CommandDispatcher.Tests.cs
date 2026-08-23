using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class CommandDispatcherTests
{
    private sealed record GreetCommand(string Name) : ICommand;

    private sealed record ShoutCommand(string Name) : ICommand;

    private sealed record UnhandledCommand : ICommand;

    private sealed class GreetHandler : ICommandHandler<GreetCommand>
    {
        public GreetCommand? Received { get; private set; }

        public CancellationToken ReceivedToken { get; private set; }

        public int CallCount { get; private set; }

        public Task<CommandResult> ExecuteAsync(GreetCommand command, CancellationToken cancellationToken = default)
        {
            Received = command;
            ReceivedToken = cancellationToken;
            CallCount++;
            return Task.FromResult(CommandResult.Success(new GreetCommand($"un-{command.Name}")));
        }
    }

    private sealed class ShoutHandler : ICommandHandler<ShoutCommand>
    {
        public Task<CommandResult> ExecuteAsync(ShoutCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(CommandResult.Failure($"{command.Name.ToUpperInvariant()}!"));
    }

    private static (CommandDispatcher Dispatcher, GreetHandler Greet) CreateDispatcher()
    {
        var greet = new GreetHandler();
        var services = new ServiceCollection()
            .AddSingleton<ICommandHandler<GreetCommand>>(greet)
            .AddSingleton<ICommandHandler<ShoutCommand>, ShoutHandler>()
            .BuildServiceProvider();

        return (new CommandDispatcher(services), greet);
    }

    [Fact]
    public async Task DispatchAsync_RoutesTheCommandToItsRegisteredHandler()
    {
        var (dispatcher, greet) = CreateDispatcher();
        var command = new GreetCommand("ada");

        var result = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Same(command, greet.Received);
        Assert.True(result.IsSuccess);
        Assert.Equal(new GreetCommand("un-ada"), result.Inverse);
    }

    [Fact]
    public async Task DispatchAsync_PicksTheHandlerByTheCommandsRuntimeType()
    {
        var (dispatcher, greet) = CreateDispatcher();

        // Declared as ICommand, so only the runtime type can pick the handler.
        ICommand shout = new ShoutCommand("ada");
        var result = await dispatcher.DispatchAsync(shout, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("ADA!", result.Error);
        Assert.Equal(0, greet.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_PassesTheCancellationTokenThrough()
    {
        var (dispatcher, greet) = CreateDispatcher();
        using var cts = new CancellationTokenSource();

        await dispatcher.DispatchAsync(new GreetCommand("ada"), cts.Token);

        Assert.Equal(cts.Token, greet.ReceivedToken);
    }

    [Fact]
    public async Task DispatchAsync_WithNoRegisteredHandler_ThrowsNamingTheCommand()
    {
        var (dispatcher, _) = CreateDispatcher();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new UnhandledCommand(), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(UnhandledCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_WithANullCommand_Throws()
    {
        var (dispatcher, _) = CreateDispatcher();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_CalledRepeatedly_KeepsRoutingCorrectly()
    {
        // The invoker cache is static and keyed by command type; repeated dispatches must keep
        // hitting the right handler rather than a stale entry from another test or instance.
        var (dispatcher, greet) = CreateDispatcher();

        await dispatcher.DispatchAsync(new GreetCommand("one"), TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(new GreetCommand("two"), TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(new GreetCommand("three"), TestContext.Current.CancellationToken);

        Assert.Equal(3, greet.CallCount);
        Assert.Equal(new GreetCommand("three"), greet.Received);
    }

    [Fact]
    public async Task DispatchAsync_FromASecondDispatcher_ResolvesFromItsOwnServices()
    {
        // Guards the shared static invoker cache: it must not capture the first dispatcher's
        // service provider, or a second one would silently run the first one's handlers.
        var (first, firstGreet) = CreateDispatcher();
        var (second, secondGreet) = CreateDispatcher();

        await first.DispatchAsync(new GreetCommand("first"), TestContext.Current.CancellationToken);
        await second.DispatchAsync(new GreetCommand("second"), TestContext.Current.CancellationToken);

        Assert.Equal(1, firstGreet.CallCount);
        Assert.Equal(1, secondGreet.CallCount);
        Assert.Equal(new GreetCommand("first"), firstGreet.Received);
        Assert.Equal(new GreetCommand("second"), secondGreet.Received);
    }

    [Fact]
    public void Constructor_WithNullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CommandDispatcher(null!));
    }
}
