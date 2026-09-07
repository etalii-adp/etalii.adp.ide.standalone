using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.History.Tests;

public class CommandDispatcherTests
{

    private static (CommandDispatcher Dispatcher, CommandDispatcherGreetHandler Greet) CreateDispatcher()
    {
        var greet = new CommandDispatcherGreetHandler();
        var services = new ServiceCollection()
            .AddSingleton<ICommandHandler<CommandDispatcherGreetCommand>>(greet)
            .AddSingleton<ICommandHandler<CommandDispatcherShoutCommand>, CommandDispatcherShoutHandler>()
            .BuildServiceProvider();

        return (new CommandDispatcher(services), greet);
    }

    [Fact]
    public async Task DispatchAsync_RoutesTheCommandToItsRegisteredHandler()
    {
        // Arrange.
        var (dispatcher, greet) = CreateDispatcher();
        var command = new CommandDispatcherGreetCommand("ada");

        // Act.
        var result = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Same(command, greet.Received);
        Assert.True(result.IsSuccess);
        Assert.Equal(new CommandDispatcherGreetCommand("un-ada"), result.Inverse);
    }

    [Fact]
    public async Task DispatchAsync_PicksTheHandlerByTheCommandsRuntimeType()
    {
        // Arrange.
        var (dispatcher, greet) = CreateDispatcher();

        // Act.
        // Declared as ICommand, so only the runtime type can pick the handler.
        ICommand shout = new CommandDispatcherShoutCommand("ada");
        var result = await dispatcher.DispatchAsync(shout, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("ADA!", result.Error);
        Assert.Equal(0, greet.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_PassesTheCancellationTokenThrough()
    {
        // Arrange.
        var (dispatcher, greet) = CreateDispatcher();
        using var cts = new CancellationTokenSource();

        // Act.
        await dispatcher.DispatchAsync(new CommandDispatcherGreetCommand("ada"), cts.Token);

        // Assert.
        Assert.Equal(cts.Token, greet.ReceivedToken);
    }

    [Fact]
    public async Task DispatchAsync_WithNoRegisteredHandler_ThrowsNamingTheCommand()
    {
        // Arrange.
        var (dispatcher, _) = CreateDispatcher();

        // Act.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new CommandDispatcherUnhandledCommand(), TestContext.Current.CancellationToken));

        // Assert.
        Assert.Contains(nameof(CommandDispatcherUnhandledCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_WithANullCommand_Throws()
    {
        // Act.
        var (dispatcher, _) = CreateDispatcher();

        // Assert.
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_CalledRepeatedly_KeepsRoutingCorrectly()
    {
        // Arrange.
        // The invoker cache is static and keyed by command type; repeated dispatches must keep
        // hitting the right handler rather than a stale entry from another test or instance.
        var (dispatcher, greet) = CreateDispatcher();

        // Act.
        await dispatcher.DispatchAsync(new CommandDispatcherGreetCommand("one"), TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(new CommandDispatcherGreetCommand("two"), TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(new CommandDispatcherGreetCommand("three"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(3, greet.CallCount);
        Assert.Equal(new CommandDispatcherGreetCommand("three"), greet.Received);
    }

    [Fact]
    public async Task DispatchAsync_FromASecondDispatcher_ResolvesFromItsOwnServices()
    {
        // Arrange.
        // Guards the shared static invoker cache: it must not capture the first dispatcher's
        // service provider, or a second one would silently run the first one's handlers.
        var (first, firstGreet) = CreateDispatcher();
        var (second, secondGreet) = CreateDispatcher();

        // Act.
        await first.DispatchAsync(new CommandDispatcherGreetCommand("first"), TestContext.Current.CancellationToken);
        await second.DispatchAsync(new CommandDispatcherGreetCommand("second"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(1, firstGreet.CallCount);
        Assert.Equal(1, secondGreet.CallCount);
        Assert.Equal(new CommandDispatcherGreetCommand("first"), firstGreet.Received);
        Assert.Equal(new CommandDispatcherGreetCommand("second"), secondGreet.Received);
    }

    [Fact]
    public void Constructor_WithNullServices_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => new CommandDispatcher(null!));
    }
}
