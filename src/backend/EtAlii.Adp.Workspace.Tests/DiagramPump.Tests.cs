using EtAlii.Adp.Diagram;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Xunit;

namespace EtAlii.Adp.Workspace.Tests;

/// <summary>
/// A diagram stream whose tab stops reading (code-structure FR-070, SC-018, B-28): the changes raised
/// meanwhile are held up to the bound, and the one past it ends the stream, which the tab opens again
/// for a fresh baseline.
/// </summary>
public class DiagramPumpTests
{
    [Fact]
    public async Task ATabThatStopsReading_IsGivenWhatTheBoundHeld_AndThenTheStreamEnds()
    {
        // Arrange: a tab that reads the baseline and the first change, and then stops.
        var session = new RaisingSession();
        var opened = new OpenedDiagram(session, "/project/diagram.test", ShortGuid.NewShortGuid());
        var written = new List<string>();
        var reading = new TaskCompletionSource();
        var stopped = new TaskCompletionSource();
        var run = Pump().RunAsync(opened, async (delta, token) =>
        {
            written.Add(delta.Remove.ElementIds.Single().Value);
            if (written.Count == 2)
            {
                stopped.SetResult();
                await reading.Task.WaitAsync(token);
            }
        }, TestContext.Current.CancellationToken);
        session.Raise("first");
        await stopped.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act: as many changes as the bound holds, and one more.
        for (var i = 0; i <= StreamBound.Capacity; i++)
        {
            session.Raise($"change {i}");
        }

        reading.SetResult();
        var ended = await Assert.ThrowsAsync<RpcException>(() => run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        // Assert: the baseline, the change being written, then exactly what the bound held.
        string[] expected = ["baseline", "first", .. Enumerable.Range(0, StreamBound.Capacity).Select(i => $"change {i}")];
        Assert.Equal(StatusCode.ResourceExhausted, ended.StatusCode);
        Assert.Equal(expected, written);
        Assert.True(session.Disposed);
    }

    // RunAsync reads only the viewport registry; everything else serves Open.
    private static DiagramPump Pump() => new(null!, null!, null!, null!, null!, new DiagramViewportRegistry(), null!);

    private sealed class RaisingSession : IDiagramSession
    {
        public event EventHandler<DiagramDeltasEventArgs>? Changed;

        public bool Disposed { get; private set; }

        public void Raise(string elementId) => Changed?.Invoke(this, new DiagramDeltasEventArgs([new DiagramRemoveDelta([elementId])]));

        public IReadOnlyList<DiagramDelta> Baseline() => [new DiagramRemoveDelta(["baseline"])];

        public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport) => [];

        public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) => Task.FromResult("");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
