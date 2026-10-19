using System.Threading.Channels;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Workspace.Tests;

/// <summary>
/// The watcher-to-store bridge on its own: a disk change to a tracked body or registration
/// file reaches the registered reloader, and nothing else does (modular-text-editors
/// Requirements 5.3, 5.5 - the store side of "the file on disk is the tie-breaker").
/// </summary>
public class DiagramDocumentReloadBridgeTests : IDisposable
{
    private readonly FileWriter _writer = new();

    private static readonly TimeSpan _arrival = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _silence = TimeSpan.FromMilliseconds(300);

    private readonly string _root;
    private readonly ProjectFileEvents _events = new();

    public DiagramDocumentReloadBridgeTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _events.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Records every reload and every deletion it is told of, for the test to await.</summary>
    private sealed class RecordingReloader : IDiagramDocumentReloader
    {
        private readonly Channel<(string RootPath, string BodyPath)> _reloads = Channel.CreateUnbounded<(string, string)>();
        private readonly Channel<string> _deletions = Channel.CreateUnbounded<string>();

        public ToolOrigin Origin { get; } = new("test", "sample");

        /// <summary>Holds the next reload of this body until <see cref="Gate"/> completes; null holds none.</summary>
        public string? Held { get; set; }

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ReloadAsync(ProjectRoot root, string bodyPath, CancellationToken cancellationToken)
        {
            if (string.Equals(bodyPath, Held, StringComparison.OrdinalIgnoreCase))
            {
                Held = null;
                Holding.TrySetResult();
                await Gate.Task.WaitAsync(cancellationToken);
            }

            _reloads.Writer.TryWrite((root.FullPath, bodyPath));
        }

        public Task BodyDeletedAsync(ProjectRoot root, string bodyPath, CancellationToken cancellationToken)
        {
            _deletions.Writer.TryWrite(bodyPath);
            return Task.CompletedTask;
        }

        /// <summary>Every reload recorded so far, without waiting for more.</summary>
        public List<string> Recorded()
        {
            var recorded = new List<string>();
            while (_reloads.Reader.TryRead(out var reload))
            {
                recorded.Add(reload.BodyPath);
            }

            return recorded;
        }

        public async Task<(string RootPath, string BodyPath)> NextAsync(CancellationToken cancellationToken) =>
            await _reloads.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_arrival, cancellationToken);

        public async Task<string> NextDeletionAsync(CancellationToken cancellationToken) =>
            await _deletions.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_arrival, cancellationToken);

        public async Task<bool> StaysQuietAsync(CancellationToken cancellationToken) =>
            !await Eventually.TrueAsync(() => _reloads.Reader.TryPeek(out _), cancellationToken, _silence);

        public async Task<bool> NoDeletionAsync(CancellationToken cancellationToken) =>
            !await Eventually.TrueAsync(() => _deletions.Reader.TryPeek(out _), cancellationToken, _silence);
    }

    [Fact]
    public async Task ATrackedBodyThatIsDeleted_IsReportedAsDeleted_NotReloaded()
    {
        // A body that is gone is an empty diagram - but a store keeps its last good document
        // through a read that fails, so "gone" has to arrive as its own signal, from the watcher's
        // Deleted event, or the deleted diagram is drawn forever.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);

        File.Delete(bodyPath);

        Assert.Equal(bodyPath, await reloader.NextDeletionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATrackedBodyRenamedAway_IsReportedAsDeleted()
    {
        // A user renaming or moving the body leaves no body at the tracked path, exactly as a
        // delete does - and raises Renamed, not Deleted.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);

        File.Move(bodyPath, IoPath.Combine(_root, "renamed.dsl"));

        Assert.Equal(bodyPath, await reloader.NextDeletionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASaveThatReplacesTheBody_ReloadsIt_AndIsNeverReportedAsDeleted()
    {
        // The must-not-catch half. File.Replace renames the body away to <body>~RF<hex>.TMP for the
        // instant of a save; read as a delete, every external save would blank the diagram.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);

        for (var i = 0; i < 20; i++)
        {
            await _writer.SaveAsync(bodyPath, $"body {i}", TestContext.Current.CancellationToken);
        }

        (_, string reloadedBody) = await reloader.NextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyPath, reloadedBody);
        Assert.True(await reloader.NoDeletionAsync(TestContext.Current.CancellationToken), "A save that replaced the body was reported as its deletion.");
    }

    [Fact]
    public async Task AnExternalWriteToATrackedBody_ReachesTheReloader()
    {
        // Arrange.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "before", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);

        // Act: the write an external tool makes - in place, no store involved.
        await File.WriteAllTextAsync(bodyPath, "after", TestContext.Current.CancellationToken);

        // Assert.
        (string reloadedRoot, string reloadedBody) = await reloader.NextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_root, reloadedRoot);
        Assert.Equal(bodyPath, reloadedBody);
    }

    [Fact]
    public async Task AWriteToTheRegistrationFile_ReloadsItsBody()
    {
        // Arrange.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        var registrationPath = IoPath.Combine(_root, "sample.adp");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(registrationPath, "test/sample", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath, reloader.Origin);

        // Act.
        await File.WriteAllTextAsync(registrationPath, "test/sample\nview: other", TestContext.Current.CancellationToken);

        // Assert: the body is what the sessions show, so that is what gets re-read.
        (_, string reloadedBody) = await reloader.NextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyPath, reloadedBody);
    }

    [Fact]
    public async Task AWriteToAnUntrackedFile_ReloadsNothing()
    {
        // Arrange.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);

        // Act: a neighbour the bridge was never told about.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "unrelated.txt"), "noise", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await reloader.StaysQuietAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReloadStillReading_HoldsNeitherTheWatcherNorTheOrder_OfWhatChangesNext()
    {
        // A body still being written is retried on the clock (code-structure FR-050). Before, the
        // retries slept on the watcher's thread, so every other subscriber of the root heard nothing
        // until they were spent; and a read is not to be overtaken by a later one either.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var first = IoPath.Combine(_root, "first.dsl");
        var second = IoPath.Combine(_root, "second.dsl");
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(first, "body", cancellationToken);
        await File.WriteAllTextAsync(second, "body", cancellationToken);
        bridge.Track(ProjectRoot.From(_root), first, registrationPath: null, reloader.Origin);
        bridge.Track(ProjectRoot.From(_root), second, registrationPath: null, reloader.Origin);
        var neighbour = IoPath.Combine(_root, "neighbour.txt");
        var heard = Channel.CreateUnbounded<string>();
        using var listening = _events.Subscribe(_root, change => heard.Writer.TryWrite(change.Path));
        reloader.Held = first;

        // Act: the first body's read is held; then the second body and a neighbour change.
        await File.WriteAllTextAsync(first, "changed", cancellationToken);
        await reloader.Holding.Task.WaitAsync(_arrival, cancellationToken);
        await File.WriteAllTextAsync(second, "changed", cancellationToken);
        await File.WriteAllTextAsync(neighbour, "noise", cancellationToken);

        // Assert: the watcher still tells the root's other subscribers...
        Assert.True(await Eventually.TrueAsync(() => heard.Reader.TryRead(out var path) && path == neighbour, cancellationToken, _arrival), "The held read kept the watcher's thread from telling the root's other subscribers.");

        // ...while the second body waits for the first body's read to finish.
        Assert.Empty(reloader.Recorded());
        reloader.Gate.SetResult();
        Assert.Equal(first, (await reloader.NextAsync(cancellationToken)).BodyPath);
        while ((await reloader.NextAsync(cancellationToken)).BodyPath != second)
        {
            // The first body's own later change, heard while it was read.
        }
    }

    [Fact]
    public async Task ABurstOfChangesToABodyWhileItIsRead_IsAnsweredByOneMoreRead()
    {
        // What waits is bounded by the documents tracked, not by the events heard (code-structure
        // FR-070): every change to a body heard while it is read is answered by one read of the disk
        // after it, which is all any of those reads would have found.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(bodyPath, "body", cancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, reloader.Origin);
        var marker = IoPath.Combine(_root, "marker.txt");
        var heard = Channel.CreateUnbounded<string>();
        using var listening = _events.Subscribe(_root, change => heard.Writer.TryWrite(change.Path));
        reloader.Held = bodyPath;

        // Act: the read is held; twenty more changes arrive, and the marker after them says the
        // watcher has passed them all on.
        await File.WriteAllTextAsync(bodyPath, "changed", cancellationToken);
        await reloader.Holding.Task.WaitAsync(_arrival, cancellationToken);
        for (var i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(bodyPath, $"changed {i}", cancellationToken);
        }

        await File.WriteAllTextAsync(marker, "after", cancellationToken);
        Assert.True(await Eventually.TrueAsync(() => heard.Reader.TryRead(out var path) && path == marker, cancellationToken, _arrival));
        reloader.Gate.SetResult();

        // Assert: the held read, then one for everything heard while it was held.
        Assert.Equal(bodyPath, (await reloader.NextAsync(cancellationToken)).BodyPath);
        Assert.Equal(bodyPath, (await reloader.NextAsync(cancellationToken)).BodyPath);
        Assert.True(await reloader.StaysQuietAsync(cancellationToken), "A change heard while the body was read was answered by more than one read.");
    }

    [Fact]
    public async Task ADeletionWaitingItsTurn_IsNotUndoneByALaterChangeToTheRegistration()
    {
        // A store keeps its last good document through a read that cannot find the body, so the
        // later change folded into the waiting deletion must still end the document.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var other = IoPath.Combine(_root, "other.dsl");
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        var registrationPath = IoPath.Combine(_root, "sample.adp");
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(other, "body", cancellationToken);
        await File.WriteAllTextAsync(bodyPath, "body", cancellationToken);
        await File.WriteAllTextAsync(registrationPath, "test/sample", cancellationToken);
        bridge.Track(ProjectRoot.From(_root), other, registrationPath: null, reloader.Origin);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath, reloader.Origin);
        var heard = Channel.CreateUnbounded<string>();
        using var listening = _events.Subscribe(_root, change => heard.Writer.TryWrite(change.Path));
        reloader.Held = other;

        // Act: while another body is read, this one is deleted and its registration then changes.
        await File.WriteAllTextAsync(other, "changed", cancellationToken);
        await reloader.Holding.Task.WaitAsync(_arrival, cancellationToken);
        File.Delete(bodyPath);
        Assert.True(await Eventually.TrueAsync(() => heard.Reader.TryRead(out var path) && path == bodyPath, cancellationToken, _arrival));
        await File.WriteAllTextAsync(registrationPath, "test/sample\nview: other", cancellationToken);
        Assert.True(await Eventually.TrueAsync(() => heard.Reader.TryRead(out var path) && path == registrationPath, cancellationToken, _arrival));
        reloader.Gate.SetResult();

        // Assert.
        Assert.Equal(bodyPath, await reloader.NextDeletionAsync(cancellationToken));
    }

    [Fact]
    public async Task ADisposedBridge_StopsReading()
    {
        // Arrange.
        var bridge = new DiagramDocumentReloadBridge([new RecordingReloader()], _events);

        // Act.
        bridge.Dispose();
        bridge.Dispose();

        // Assert: the loop ends, rather than being left running unobserved (code-structure FR-070).
        await bridge.Completion.WaitAsync(_arrival, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnOriginWithoutAReloader_IsLeftAlone()
    {
        // Arrange: the ansible shape - a diagram type that watches its own folder already and
        // registers no reloader. Tracking it must neither throw nor reach the wrong module.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader], _events);
        var bodyPath = IoPath.Combine(_root, "other.yml");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(ProjectRoot.From(_root), bodyPath, registrationPath: null, new ToolOrigin("test", "unregistered"));

        // Act.
        await File.WriteAllTextAsync(bodyPath, "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await reloader.StaysQuietAsync(TestContext.Current.CancellationToken));
    }
}
