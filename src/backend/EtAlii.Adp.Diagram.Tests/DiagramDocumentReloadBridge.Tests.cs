using System.Threading.Channels;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The watcher-to-store bridge on its own: a disk change to a tracked body or registration
/// file reaches the registered reloader, and nothing else does (modular-text-editors
/// Requirements 5.3, 5.5 - the store side of "the file on disk is the tie-breaker").
/// </summary>
public class DiagramDocumentReloadBridgeTests : IDisposable
{
    private static readonly TimeSpan _arrival = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _silence = TimeSpan.FromMilliseconds(300);

    private readonly string _root;

    public DiagramDocumentReloadBridgeTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
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

        public DiagramOrigin Origin { get; } = new("test", "sample");

        public void Reload(string rootPath, string bodyPath) => _reloads.Writer.TryWrite((rootPath, bodyPath));

        public void BodyDeleted(string rootPath, string bodyPath) => _deletions.Writer.TryWrite(bodyPath);

        public async Task<(string RootPath, string BodyPath)> NextAsync(CancellationToken cancellationToken) =>
            await _reloads.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_arrival, cancellationToken);

        public async Task<string> NextDeletionAsync(CancellationToken cancellationToken) =>
            await _deletions.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_arrival, cancellationToken);

        public async Task<bool> StaysQuietAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(_silence, cancellationToken);
            return !_reloads.Reader.TryRead(out _);
        }

        public async Task<bool> NoDeletionAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(_silence, cancellationToken);
            return !_deletions.Reader.TryRead(out _);
        }
    }

    [Fact]
    public async Task ATrackedBodyThatIsDeleted_IsReportedAsDeleted_NotReloaded()
    {
        // A body that is gone is an empty diagram - but a store keeps its last good document
        // through a read that fails, so "gone" has to arrive as its own signal, from the watcher's
        // Deleted event, or the deleted diagram is drawn forever.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, reloader.Origin);

        File.Delete(bodyPath);

        Assert.Equal(bodyPath, await reloader.NextDeletionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATrackedBodyRenamedAway_IsReportedAsDeleted()
    {
        // A user renaming or moving the body leaves no body at the tracked path, exactly as a
        // delete does - and raises Renamed, not Deleted.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, reloader.Origin);

        File.Move(bodyPath, IoPath.Combine(_root, "renamed.dsl"));

        Assert.Equal(bodyPath, await reloader.NextDeletionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASaveThatReplacesTheBody_ReloadsIt_AndIsNeverReportedAsDeleted()
    {
        // The must-not-catch half. File.Replace renames the body away to <body>~RF<hex>.TMP for the
        // instant of a save; read as a delete, every external save would blank the diagram.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, reloader.Origin);

        for (var i = 0; i < 20; i++)
        {
            AdpFileWriter.Save(bodyPath, $"body {i}");
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
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "before", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, reloader.Origin);

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
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        var registrationPath = IoPath.Combine(_root, "sample.adp");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(registrationPath, "test/sample", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath, reloader.Origin);

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
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "sample.dsl");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, reloader.Origin);

        // Act: a neighbour the bridge was never told about.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "unrelated.txt"), "noise", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await reloader.StaysQuietAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOriginWithoutAReloader_IsLeftAlone()
    {
        // Arrange: the ansible shape - a diagram type that watches its own folder already and
        // registers no reloader. Tracking it must neither throw nor reach the wrong module.
        var reloader = new RecordingReloader();
        using var bridge = new DiagramDocumentReloadBridge([reloader]);
        var bodyPath = IoPath.Combine(_root, "other.yml");
        await File.WriteAllTextAsync(bodyPath, "body", TestContext.Current.CancellationToken);
        bridge.Track(_root, bodyPath, registrationPath: null, new DiagramOrigin("test", "unregistered"));

        // Act.
        await File.WriteAllTextAsync(bodyPath, "changed", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await reloader.StaysQuietAsync(TestContext.Current.CancellationToken));
    }
}
