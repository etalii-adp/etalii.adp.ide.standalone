using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Problems.Tests;

/// <summary>
/// Maintenance over a real folder and its real watcher, settling on a fake clock: the watcher's
/// events arrive in their own time, and the clock is advanced while a test waits for what they
/// cause, so no test waits for a settle delay to pass (code-structure FR-051).
/// </summary>
public class ProblemMaintenanceTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static readonly ToolOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

    private static readonly ToolOrigin FolderType = new("fixture", "folder");
    private static readonly DiagramDefinition FolderDefinition =
        new(FolderType, "A folder-subject type", Subject: DiagramSubject.Folder);

    private readonly string _root;
    private readonly ProblemMaintenanceRecordingStore _store = new();
    private readonly ProblemMaintenanceCountingValidator _validator = new(Mindmap);
    private readonly ProblemMaintenanceFolderValidator _folderValidator =
        new(FolderType, IoPath.Combine("infrastructure", "roles", "web", "meta", "main.yml"));
    private readonly FakeTimeProvider _clock = new();
    private readonly ProjectFileEvents _events = new();
    private readonly ProblemMaintenance _maintenance;

    public ProblemMaintenanceTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog(MindmapDefinition, FolderDefinition));
        var projectValidator = new ProjectValidator(router, new DiagramValidators([_validator, _folderValidator]));
        _maintenance = new ProblemMaintenance(_store, projectValidator, router, _events, _clock, SettleDelay);
    }

    public void Dispose()
    {
        _maintenance.Dispose();
        _events.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void ATrackedRoot_ListensThroughTheRootsOneWatcher()
    {
        // Arrange: somebody else - a connection's hierarchy - watching the root already.
        var other = _events.Subscribe(_root, _ => { });
        var watcher = _events.Watcher(_root);

        // Act.
        _maintenance.Track(_root);
        other.Dispose();

        // Assert: still the one watcher, now kept by the problems alone.
        Assert.Equal(1, _events.WatchedRoots());
        Assert.Same(watcher, _events.Watcher(_root));
    }

    [Fact]
    public async Task AChangedDiagramCostsOneFilesValidation_NotATraversal()
    {
        // Arrange.
        CreatePair("a");
        CreatePair("b");
        _maintenance.Track(_root);

        await File.AppendAllTextAsync(IoPath.Combine(_root, "a.mm"), " and more", TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        (_, object payload) = await WaitForMutation("ReplaceFor");
        // One file changed, one validation - project-wide traversal would have made two.
        Assert.Equal(1, _validator.Calls);
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.Contains("a.adp", covered);
        Assert.DoesNotContain(covered, path => path.StartsWith('b'));
    }

    [Fact]
    public async Task ARemovedDiagramLosesItsEntries()
    {
        // Arrange.
        CreatePair("gone");
        _maintenance.Track(_root);

        File.Delete(IoPath.Combine(_root, "gone.adp"));

        // Act and assert, step by step.
        (_, object payload) = await WaitForMutation("Remove");
        Assert.Equal("gone.adp", payload);
    }

    [Fact]
    public async Task ARenamedDiagramKeepsItsProblemsAtTheNewPath()
    {
        // Arrange.
        CreatePair("old");
        _maintenance.Track(_root);

        File.Move(IoPath.Combine(_root, "old.adp"), IoPath.Combine(_root, "new.adp"));

        // Act and assert, step by step.
        (_, object payload) = await WaitForMutation("Move");
        Assert.Equal(("old.adp", "new.adp"), payload);
        // Moved, never dropped: the problems were not re-reported as unchecked.
        Assert.DoesNotContain(_store.Mutations, mutation => mutation.Kind == "Remove");
    }

    [Fact]
    public async Task RapidChangesCoalesceIntoOneValidation()
    {
        // Arrange. The burst is handed to the root directly, so the clock alone decides when it
        // settles; the watcher sees no change of its own here.
        CreatePair("busy");
        using var root = new TrackedProblemRoot(_root, _maintenance);
        var busy = IoPath.Combine(_root, "busy.mm");

        // Act and assert, step by step. Every change starts the window over.
        for (var edit = 0; edit < 5; edit++)
        {
            root.Enqueue(busy);
            _clock.Advance(SettleDelay - TimeSpan.FromMilliseconds(1));
        }

        Assert.Empty(_store.Mutations);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForMutation("ReplaceFor");
        _clock.Advance(SettleDelay + SettleDelay);
        Assert.Equal(1, _validator.Calls);
    }

    [Fact]
    public async Task AChangeToAFileNoTypeClaims_IsIgnored()
    {
        // Arrange.
        CreatePair("real");
        _maintenance.Track(_root);

        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        // The real pair's change proves events flow; the .txt must not have caused anything.
        await File.AppendAllTextAsync(IoPath.Combine(_root, "real.mm"), " and more", TestContext.Current.CancellationToken);
        (_, object payload) = await WaitForMutation("ReplaceFor");
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.DoesNotContain("notes.txt", covered);
        Assert.All(_store.Mutations, mutation => Assert.Equal("ReplaceFor", mutation.Kind));
    }

    [Fact]
    public async Task AChangeInsideAFolderDiagram_RevalidatesThatDiagram()
    {
        // Arrange.
        // The file edited is not a diagram and never will be. It is part of one: the .adp two
        // levels above it marks the folder as the diagram's subject. Without the walk-up the
        // change would be validated as itself, find nothing, and clear the diagram's problems
        // without re-finding them - losing a problem rather than refreshing it.
        var folder = IoPath.Combine(_root, "infrastructure");
        var meta = IoPath.Combine(folder, "roles", "web", "meta");
        Directory.CreateDirectory(meta);
        await File.WriteAllTextAsync(IoPath.Combine(folder, "infrastructure.adp"), "fixture/folder\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(meta, "main.yml"), "dependencies: [base]\n", TestContext.Current.CancellationToken);
        _maintenance.Track(_root);

        // Act.
        await File.AppendAllTextAsync(IoPath.Combine(meta, "main.yml"), "# edited\n", TestContext.Current.CancellationToken);

        // Assert.
        (_, object payload) = await WaitForMutation("ReplaceFor");
        Assert.Equal(1, _folderValidator.Calls);
        // The validator was handed the folder, not just one MIME line of registration text.
        Assert.Equal(folder, _folderValidator.LastSubjectFolder);
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.Contains(IoPath.Combine("infrastructure", "infrastructure.adp"), covered);
        Assert.Contains(IoPath.Combine("infrastructure", "roles", "web", "meta", "main.yml"), covered);
    }

    [Fact]
    public async Task AChangeOutsideAnyFolderDiagram_IsStillIgnored()
    {
        // Arrange.
        // The walk-up must not turn every stray file into a validation: this one has no
        // folder-subject registration above it, so it stays as ignorable as it always was.
        CreatePair("real");
        _maintenance.Track(_root);

        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);

        // Act.
        await File.AppendAllTextAsync(IoPath.Combine(_root, "real.mm"), " and more", TestContext.Current.CancellationToken);

        // Assert.
        await WaitForMutation("ReplaceFor");
        Assert.Equal(0, _folderValidator.Calls);
    }

    [Fact]
    public void AFileReplaceBackup_IsNoFileToHoldProblems()
    {
        // Arrange (behaviour change B-06). File.Replace moves the file aside to <name>~RF<hex>.TMP
        // for the instant of a save and deletes the backup after. Taken at face value the rename
        // moved the file's problems onto the backup name and the delete then removed them.
        CreatePair("saved");
        using var root = new TrackedProblemRoot(_root, _maintenance);
        var body = IoPath.Combine(_root, "saved.mm");
        var backup = body + "~RF1a2b3c.TMP";

        // Act: the two events of the swap that name the backup.
        _maintenance.OnRenamed(root, body, backup);
        _maintenance.OnDeleted(root, backup);

        // Assert: the problems stayed with the file, and no problem was recorded for the backup.
        Assert.DoesNotContain(_store.Mutations, mutation => mutation.Kind is "Move" or "Remove");
    }

    [Fact]
    public async Task DisposeAsync_StopsTheRevalidationUnderWay_AndReturnsOnlyOnceItHas()
    {
        // Arrange: one burst of two changed diagrams, its re-validation held as it records the
        // first verdict - a point no cancellation reaches.
        CreatePair("a");
        CreatePair("b");
        var recording = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        _store.ReplacingFor = () =>
        {
            recording.TrySetResult();
            // ReSharper disable once AccessToDisposedClosure - Reason: this runs inside the re-validation, which DisposeAsync awaits below before the using disposes `release`.
            release.Wait(WaitLimit, TestContext.Current.CancellationToken);
        };
        using var root = new TrackedProblemRoot(_root, _maintenance);
        root.Enqueue(IoPath.Combine(_root, "a.mm"));
        root.Enqueue(IoPath.Combine(_root, "b.mm"));
        _clock.Advance(SettleDelay);
        await recording.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        // Act.
        var disposing = _maintenance.DisposeAsync().AsTask();

        // Assert: still closing while the re-validation finishes; closed once it has, without
        // validating the burst's second diagram.
        var first = await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.False(first == disposing, "Maintenance closed while a re-validation it started was still running.");
        release.Set();
        await disposing.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Single(_store.Mutations, mutation => mutation.Kind == "ReplaceFor");
    }

    // ---- plumbing ----------------------------------------------------------------------

    [Fact]
    public async Task AChangeReachedThroughALinkThatLeavesTheRoot_IsNotRevalidated()
    {
        // Arrange: a link inside the project that leads to a diagram outside it (D6). The
        // maintenance itself refuses it, rather than leaving the refusal to the validator and
        // then recording what the validator said about a file that is not the project's.
        var elsewhere = Directory.CreateTempSubdirectory("problem-maintenance-elsewhere-").FullName;
        try
        {
            await File.WriteAllTextAsync(IoPath.Combine(elsewhere, "outside.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(IoPath.Combine(elsewhere, "outside.mm"), "the document", TestContext.Current.CancellationToken);
            var link = IoPath.Combine(_root, "escape");
            try
            {
                Directory.CreateSymbolicLink(link, elsewhere);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && OperatingSystem.IsWindows())
            {
                Assert.Skip($"This process may not create a symbolic link: {exception.Message}");
            }

            using var root = new TrackedProblemRoot(_root, _maintenance);

            // Act.
            await _maintenance.RevalidateAsync(root, [IoPath.Combine(link, "outside.adp")], TestContext.Current.CancellationToken);

            // Assert.
            Assert.Equal(0, _validator.Calls);
            Assert.DoesNotContain(_store.Mutations, mutation => mutation.Kind == "ReplaceFor");
        }
        finally
        {
            TestFolder.TryDelete(elsewhere);
        }
    }

    private void CreatePair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), "the document");
    }

    /// <summary>
    /// Waits for the store to see <paramref name="kind"/>, advancing the clock a settle delay at a
    /// time meanwhile: whatever the watcher has handed over by then settles, and nothing else does.
    /// </summary>
    private async Task<(string Kind, object Payload)> WaitForMutation(string kind)
    {
        Assert.True(
            await Eventually.TrueAsync(
                () =>
                {
                    _clock.Advance(SettleDelay);
                    return _store.Mutations.Any(mutation => mutation.Kind == kind);
                },
                TestContext.Current.CancellationToken,
                WaitLimit,
                TimeSpan.FromMilliseconds(50)),
            $"No {kind} mutation arrived within {WaitLimit}.");
        return _store.Mutations.First(mutation => mutation.Kind == kind);
    }

}
