using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.History.Tests;

/// <summary>
/// A command that succeeded with something worth saying tells the project, once, and a command
/// with nothing to say stays quiet.
/// </summary>
/// <remarks>
/// This is the one place every module's commands pass through, which is why the notice is sent
/// from here. A warning threaded back through <c>IDiagramSession</c> and each response message
/// would have to be added to every module that exists and remembered by every module written
/// afterwards; here it is written once and covers all of them, including modules that do not
/// exist yet.
/// </remarks>
public class HistoryStackNoticesTests
{
    private sealed class RecordingSink : IContextNoticeSink
    {
        public List<(string RootPath, string Message)> Notified { get; } = [];

        public void Notify(string rootPath, string message) => Notified.Add((rootPath, message));
    }

    private sealed record NothingCommand : ICommand;

    private sealed class StubDispatcher(CommandResult result) : ICommandDispatcher
    {
        public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    [Fact]
    public async Task ASuccessCarryingAWarning_TellsTheProject()
    {
        // Arrange.
        var sink = new RecordingSink();
        var result = CommandResult.Success(new NothingCommand(), "The new position could not be saved.");
        var stack = new HistoryStack(new StubDispatcher(result), rootPath: @"C:\project", notices: sink);

        // Act.
        var executed = await stack.ExecuteAsync(new NothingCommand(), TestContext.Current.CancellationToken);

        // Assert: the edit still succeeded - that is the whole point - and the loss was reported.
        Assert.True(executed.IsSuccess);
        var (rootPath, message) = Assert.Single(sink.Notified);
        Assert.Equal(@"C:\project", rootPath);
        Assert.Equal("The new position could not be saved.", message);
    }

    [Fact]
    public async Task AnOrdinarySuccess_SaysNothing()
    {
        // A notice for every successful command would be noise, and noise is how a real one gets
        // ignored.
        var sink = new RecordingSink();
        var stack = new HistoryStack(
            new StubDispatcher(CommandResult.Success(new NothingCommand())),
            rootPath: @"C:\project",
            notices: sink);

        await stack.ExecuteAsync(new NothingCommand(), TestContext.Current.CancellationToken);

        Assert.Empty(sink.Notified);
    }

    [Fact]
    public async Task ARejectedCommand_SaysNothingEither()
    {
        // A failure already answers the caller with its reason; a notice as well would report the
        // same thing twice, in two places, and the second one cannot be acted on.
        var sink = new RecordingSink();
        var stack = new HistoryStack(
            new StubDispatcher(CommandResult.Failure("No.")),
            rootPath: @"C:\project",
            notices: sink);

        await stack.ExecuteAsync(new NothingCommand(), TestContext.Current.CancellationToken);

        Assert.Empty(sink.Notified);
    }
}
