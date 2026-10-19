using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

/// <summary>
/// A session's first read of its document holds no thread (code-structure FR-050, T283): the
/// baseline waits for the store without blocking, and a viewport reported before it has read
/// anything is answered with nothing rather than with a read of its own.
/// </summary>
public class DocumentDiagramSessionTests
{
    [Fact]
    public async Task TheBaseline_WaitsForTheStoreWithoutHoldingTheThread()
    {
        // Arrange.
        var store = new PendingStore();
        await using var session = new Session(store);

        // Act: the store has not answered yet.
        var baseline = session.BaselineAsync(TestContext.Current.CancellationToken);

        // Assert: the call returned while the read is still under way, and delivers once it ends.
        Assert.False(baseline.IsCompleted, "The baseline read its document holding the thread.");
        store.Answer("read");
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(await baseline));
        Assert.Equal("read", Assert.Single(add.Elements).Id);
    }

    [Fact]
    public async Task AViewportReportedBeforeTheBaseline_IsAnsweredWithNothing_AndReadsNothing()
    {
        // Arrange.
        var store = new PendingStore();
        await using var session = new Session(store);

        // Act.
        var before = session.UpdateView(DiagramViewport.Unbounded);
        store.Answer("read");
        await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Assert: nothing to say before the document was read; the baseline then delivered it.
        Assert.Empty(before);
        Assert.Empty(session.UpdateView(DiagramViewport.Unbounded));
    }

    /// <summary>Draws one element named after the model.</summary>
    private sealed class Session(PendingStore store) : DocumentDiagramSession<string, string>("body.test", store, history: null)
    {
        protected override IReadOnlyList<DiagramElement> Render(string model, DiagramViewport viewport) =>
            [new(model, 0, 0, "test/element", "", ReadOnlyMemory<byte>.Empty)];
    }

    /// <summary>A store whose read ends when the test says, and that refuses to read holding a thread.</summary>
    private sealed class PendingStore : IReadableDiagramDocumentStore<string, string>
    {
        private readonly TaskCompletionSource<DocumentEntry<string, string>> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DocumentEntry<string, string>? _held;

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Answer(string model) => _read.SetResult(new DocumentEntry<string, string>(model, model));

        public DocumentEntry<string, string> GetOrLoad(string path) =>
            throw new InvalidOperationException("The session read its document holding a thread.");

        public async ValueTask<DocumentEntry<string, string>> GetOrLoadAsync(string path, CancellationToken cancellationToken)
        {
            _held = await _read.Task.WaitAsync(cancellationToken);
            return _held;
        }

        public DocumentEntry<string, string>? Get(string path) => _held;

        public void Reload(string path)
        {
        }

        public void BodyDeleted(string path)
        {
        }

        public void Forget(string path)
        {
        }
    }
}
