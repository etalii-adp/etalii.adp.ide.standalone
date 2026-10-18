using System.Threading.Channels;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Xunit;

namespace EtAlii.Adp.Workspace.Tests;

/// <summary>
/// The one helper that opens, pumps and closes a stream on a tab's connection, for diagram and table
/// streams alike, and the one place that asks whether a connection is the caller's (code-structure FR-069).
/// </summary>
public class WorkspaceConnectionTests
{
    private static readonly ShortGuid Owner = ShortGuid.NewShortGuid();

    private sealed class FakeSession : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static StreamMap<FakeSession> Map(Func<FakeSession, CancellationToken, Task> run, bool refuses = true) => new()
    {
        Kind = "Test",
        RunAsync = (session, _, _, token) => run(session, token),
        Ended = streamId => new WorkspaceMessage { Diagram = new DiagramStreamMessage { StreamId = streamId, Ended = new DiagramStreamEnded() } },
        Refused = refuses
            ? (streamId, refusal) => new WorkspaceMessage { Diagram = new DiagramStreamMessage { StreamId = streamId, Refused = new DiagramStreamRefused { Code = (int)refusal.StatusCode } } }
            : null,
    };

    [Fact]
    public void Find_AnswersOnlyTheUserWhoseConnectionItIs()
    {
        // Arrange.
        var connections = new WorkspaceConnections();
        var watchId = ShortGuid.NewShortGuid();
        var other = ShortGuid.NewShortGuid();
        using var connection = connections.Register(watchId, Owner, Channel.CreateUnbounded<WorkspaceMessage>().Writer, CancellationToken.None);

        // Act.
        var found = connections.Find(watchId, Owner);
        var foundByAnother = connections.Find(watchId, other);
        var refusal = Assert.Throws<RpcException>(() => connections.Require(watchId, other));

        // Assert.
        Assert.Same(connection, found);
        Assert.Null(foundByAnother);
        Assert.Equal(StatusCode.Unavailable, refusal.StatusCode);
    }

    [Fact]
    public async Task CloseAsync_ReturnsOnlyOnceEveryPumpHasReturned()
    {
        // Arrange: a pump that is slow to let go of its session once it is stopped.
        var connections = new WorkspaceConnections();
        using var connection = connections.Register(ShortGuid.NewShortGuid(), Owner, Channel.CreateUnbounded<WorkspaceMessage>().Writer, CancellationToken.None);
        var session = new FakeSession();
        var letGo = new TaskCompletionSource();
        await connection.OpenStreamAsync(ShortGuid.NewShortGuid(), session, Map(async (opened, token) =>
        {
            await using (opened)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    await letGo.Task;
                }
            }
        }));

        // Act.
        var close = connection.CloseAsync();

        // Assert: still waiting on the pump, and its session released once it does return.
        Assert.False(close.IsCompleted, "The connection closed while a stream it carried was still running.");
        letGo.SetResult();
        await close.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(session.Disposed);
        Assert.Equal(0, connection.StreamCount);
    }

    [Fact]
    public async Task APumpThatFails_TellsTheClientTheStreamEnded()
    {
        // Arrange.
        var connections = new WorkspaceConnections();
        var channel = Channel.CreateUnbounded<WorkspaceMessage>();
        using var connection = connections.Register(ShortGuid.NewShortGuid(), Owner, channel.Writer, CancellationToken.None);
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();

        // Act.
        await connection.OpenStreamAsync((ShortGuid)streamId, new FakeSession(), Map((_, _) => throw new InvalidOperationException("The session failed.")));

        // Assert.
        Assert.True(channel.Reader.TryRead(out var message));
        Assert.Equal(streamId, message.Diagram.StreamId);
        Assert.Equal(DiagramStreamMessage.EventOneofCase.Ended, message.Diagram.EventCase);
        Assert.Equal(0, connection.StreamCount);
    }

    [Theory]
    [InlineData(true, DiagramStreamMessage.EventOneofCase.Refused)]
    [InlineData(false, DiagramStreamMessage.EventOneofCase.Ended)]
    public async Task APermanentRefusal_ReadsAsTheRefusal_OnlyWhereTheStreamHasOne(bool refuses, DiagramStreamMessage.EventOneofCase expected)
    {
        // Arrange.
        var connections = new WorkspaceConnections();
        var channel = Channel.CreateUnbounded<WorkspaceMessage>();
        using var connection = connections.Register(ShortGuid.NewShortGuid(), Owner, channel.Writer, CancellationToken.None);
        var refusal = new RpcException(new Status(PermanentRefusal.CannotOpen, "The document cannot be opened."));

        // Act.
        await connection.OpenStreamAsync(ShortGuid.NewShortGuid(), new FakeSession(), Map((_, _) => throw refusal, refuses));

        // Assert.
        Assert.True(channel.Reader.TryRead(out var message));
        Assert.Equal(expected, message.Diagram.EventCase);
    }

    [Fact]
    public async Task AStreamIdAlreadyRunning_IsRefused_AndTheSessionThatWouldHaveRunIsDisposed()
    {
        // Arrange.
        var connections = new WorkspaceConnections();
        using var connection = connections.Register(ShortGuid.NewShortGuid(), Owner, Channel.CreateUnbounded<WorkspaceMessage>().Writer, CancellationToken.None);
        var streamId = ShortGuid.NewShortGuid();
        var running = Map((_, token) => Task.Delay(Timeout.Infinite, token));
        await connection.OpenStreamAsync(streamId, new FakeSession(), running);
        var second = new FakeSession();

        // Act.
        var refusal = await Assert.ThrowsAsync<RpcException>(() => connection.OpenStreamAsync(streamId, second, running));

        // Assert.
        Assert.Equal(StatusCode.AlreadyExists, refusal.StatusCode);
        Assert.True(second.Disposed);
        Assert.Equal(1, connection.StreamCount);
        await connection.CloseAsync();
    }
}
