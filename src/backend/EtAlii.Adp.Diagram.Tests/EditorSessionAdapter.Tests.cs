using System.Text;
using EtAlii.Adp.Editor;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The bridge between the families (modular-text-editors, "no new stream"): a text file rides
/// the diagram wire as one synthetic element, and every editor behaviour maps onto the delta
/// vocabulary that already exists - Remove+Add for a change, since the closed set has no
/// Update arm and the proto may not grow one.
/// </summary>
public class EditorSessionAdapterTests
{
    private sealed class FakeEditorSession : IEditorSession
    {
        public string Content { get; init; } = "hello";
        public bool Disposed { get; private set; }

        public event EventHandler<EditorContentChangedEventArgs>? Changed;

        public void RaiseChanged(string content) => Changed?.Invoke(this, new EditorContentChangedEventArgs(content));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void Baseline_IsOneContentElementCarryingTheWholeText()
    {
        // Arrange.
        var adapter = new EditorSessionAdapter(new FakeEditorSession { Content = "hello" }, "plain");

        // Act.
        var baseline = adapter.Baseline();

        // Assert.
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(baseline));
        var element = Assert.Single(add.Elements);
        Assert.Equal("content", element.Id);
        Assert.Equal("editor/plain", element.Type);
        Assert.Equal("hello", Encoding.UTF8.GetString(element.Payload.Span));
    }

    [Fact]
    public void AnExternalChange_ReplaysAsRemoveThenAdd_OnTheSameElementId()
    {
        // Arrange: the design said "Update delta", but the vocabulary has no Update arm and
        // the proto may not grow one - the replacement pair is the honest expression.
        var session = new FakeEditorSession();
        var adapter = new EditorSessionAdapter(session, "plain");
        DiagramDeltasEventArgs? received = null;
        adapter.Changed += (_, args) => received = args;

        // Act.
        session.RaiseChanged("changed");

        // Assert.
        Assert.NotNull(received);
        Assert.Equal(2, received.Deltas.Count);
        var remove = Assert.IsType<DiagramRemoveDelta>(received.Deltas[0]);
        Assert.Equal("content", Assert.Single(remove.ElementIds));
        var add = Assert.IsType<DiagramAddDelta>(received.Deltas[1]);
        Assert.Equal("changed", Encoding.UTF8.GetString(Assert.Single(add.Elements).Payload.Span));
    }

    [Fact]
    public async Task Moves_AreRefused_BothKinds()
    {
        // Arrange.
        var adapter = new EditorSessionAdapter(new FakeEditorSession(), "plain");

        // Act and assert: text has no elements to move - the explicit refusal for the nesting
        // move, and the interface's own default refusal for the arranging one.
        Assert.NotEqual("", await adapter.MoveElementAsync("content", "x", 0, TestContext.Current.CancellationToken));
        IDiagramSession asSession = adapter;
        Assert.NotEqual("", await asSession.MoveElementToAsync("content", 1, 2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_UnhooksAndDisposesTheWrappedSession()
    {
        // Arrange.
        var session = new FakeEditorSession();
        var adapter = new EditorSessionAdapter(session, "plain");
        var received = 0;
        adapter.Changed += (_, _) => received++;

        // Act.
        await adapter.DisposeAsync();
        session.RaiseChanged("late");

        // Assert.
        Assert.True(session.Disposed);
        Assert.Equal(0, received);
    }
}
