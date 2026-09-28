using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The registry joining a MoveElement or UpdateView call to its open stream, when two streams for one
/// diagram overlap on one connection - as the client's development remount opens a second before the
/// first has closed (Peter's report, 2026-09-27: a dropped trend reverted on the next gesture and
/// nothing reached the disk, because every move answered "The diagram is not open on this connection").
/// </summary>
public class DiagramViewportRegistryTests
{
    private static readonly ShortGuid Watch = ShortGuid.NewShortGuid();
    private const string Body = @"C:\project\diagram.ghg";

    [Fact]
    public void AClosingStream_LeavesTheRegistrationOfAStreamThatReplacedIt()
    {
        var registry = new DiagramViewportRegistry();
        var first = new Session();
        var second = new Session();
        registry.Register(Watch, Body, first, _ => { });
        registry.Register(Watch, Body, second, _ => { });

        registry.Remove(Watch, Body, first);

        Assert.Same(second, registry.Find(Watch, Body));
    }

    /// <summary>The order the report's log showed: open, open, and the SECOND closes first.</summary>
    [Fact]
    public void ALaterStreamClosingFirst_LeavesTheEarlierOneFindable_AndReachedByViewReports()
    {
        var registry = new DiagramViewportRegistry();
        var first = new Session();
        var second = new Session();
        var reported = 0;
        registry.Register(Watch, Body, first, _ => reported++);
        registry.Register(Watch, Body, second, _ => { });

        registry.Remove(Watch, Body, second);

        Assert.Same(first, registry.Find(Watch, Body));
        Assert.True(registry.Report(Watch, Body, new DiagramViewport(0, 0, 1, 1)));
        Assert.Equal(1, reported);
    }

    [Fact]
    public void AClosingStream_RemovesItsOwnRegistration()
    {
        var registry = new DiagramViewportRegistry();
        var session = new Session();
        registry.Register(Watch, Body, session, _ => { });

        registry.Remove(Watch, Body, session);

        Assert.Null(registry.Find(Watch, Body));
    }

    private sealed class Session : IDiagramSession
    {
        public event EventHandler<DiagramDeltasEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DiagramDelta> Baseline() => [];

        public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport) => [];

        public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) => Task.FromResult("");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
