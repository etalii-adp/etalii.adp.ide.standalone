using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Every connection's hierarchy hears its project through the root's one watcher
/// (code-structure T148, FR-050): two watches of one project make one watcher, not two.
/// </summary>
public sealed class HierarchyServiceFileEventsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _projectFolder;

    public HierarchyServiceFileEventsTests()
    {
        _projectFolder = IoPath.Combine(_appDataRoot, "project");
        Directory.CreateDirectory(_projectFolder);
    }

    public void Dispose() => TestFolder.TryDelete(_appDataRoot);

    [Fact]
    public async Task TwoWatchesOfOneProject_ShareTheRootsOneWatcher_AndBothHearIt()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = ShortGuid.NewShortGuid();
        var projectStore = new FileProjectStore(_appDataRoot);
        var project = projectStore.Add(
            userId,
            "project",
            new PathRecord(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)));
        using var modelStore = new HierarchyModelStore(TestToolRouters.For());
        using var events = new ProjectFileEvents();
        var service = new HierarchyService(projectStore, modelStore, events);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var first = ShortGuid.NewShortGuid();
        var second = ShortGuid.NewShortGuid();
        modelStore.GetOrCreate(first, ProjectRoot.From(_projectFolder)).ListChildren(null);
        modelStore.GetOrCreate(second, ProjectRoot.From(_projectFolder)).ListChildren(null);
        var created = new[] { 0, 0 };

        // Act.
        var watches = new[]
        {
            service.RunWatchAsync(userId, new WatchHierarchyRequest { ProjectId = project.Id, WatchId = first }, (message, _) => Heard(0, message), stop.Token),
            service.RunWatchAsync(userId, new WatchHierarchyRequest { ProjectId = project.Id, WatchId = second }, (message, _) => Heard(1, message), stop.Token),
        };
        // A file written before a watch subscribed is heard by nobody, so one is written per try
        // until both have heard one.
        var written = 0;
        var bothHeard = await Eventually.TrueAsync(
            () =>
            {
                File.WriteAllText(IoPath.Combine(_projectFolder, $"new-{++written}.txt"), "");
                return Volatile.Read(ref created[0]) > 0 && Volatile.Read(ref created[1]) > 0;
            },
            cancellationToken,
            Patience);

        // Assert: both connections told of a new file, through one watcher for the root.
        Assert.True(bothHeard, "Not every watch heard a new file.");
        Assert.Equal(1, events.WatchedRoots());

        await stop.CancelAsync();
        await Task.WhenAll(watches).WaitAsync(Patience, cancellationToken);
        Assert.Equal(0, events.WatchedRoots());

        return;

        Task Heard(int watch, HierarchyMessage message)
        {
            if (message.Change?.Created is not null)
            {
                Interlocked.Increment(ref created[watch]);
            }

            return Task.CompletedTask;
        }
    }
}
