using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Xunit;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// A context watch retains its project's history for as long as it runs, and gives it back
/// however it ends - including when the discovery of the project's own actions fails or is
/// cancelled before the stream has started. The retain used to sit before the <c>try</c>, so
/// such a watch held the history for the life of the process (D5 of the 2026-10-10 analysis).
/// </summary>
public sealed class ContextServiceWatchReleaseTests : IDisposable
{
    private static readonly ShortGuid UserId = ShortGuid.NewShortGuid();
    private static readonly ShortGuid ProjectId = ShortGuid.NewShortGuid();

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("context-watch-release-");

    [Fact]
    public async Task AProjectActionsDiscoveryThatThrows_ReleasesTheHistory()
    {
        // Arrange.
        var history = new CountingHistoryStackStore();
        var service = CreateService(history, new ProjectDiscoveryFails(cancel: null));

        // Act.
        await Assert.ThrowsAsync<InvalidOperationException>(() => WatchAsync(service, TestContext.Current.CancellationToken));

        // Assert.
        Assert.Equal(1, history.Retains);
        Assert.Equal(1, history.Releases);
    }

    [Fact]
    public async Task AProjectActionsDiscoveryThatIsCancelled_ReleasesTheHistory()
    {
        // Arrange: the client goes away while the project's actions are being discovered.
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var history = new CountingHistoryStackStore();
        var service = CreateService(history, new ProjectDiscoveryFails(cancel: closed));

        // Act.
        try
        {
            await WatchAsync(service, closed.Token);
        }
        catch (OperationCanceledException)
        {
            // Either ending is the client's; what matters is what the watch gave back.
        }

        // Assert.
        Assert.Equal(1, history.Retains);
        Assert.Equal(1, history.Releases);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private ContextService CreateService(IHistoryStackStore history, IContextActionResolver actions) =>
        new(
            new OneProjectStore(new ProjectRecord(ProjectId, "project", new PathRecord(_root.FullName.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)))),
            new ContextSelectionStore(),
            new ContextSelectionResolver([]),
            actions,
            new NoProperties(),
            new ContextInteractionStore(),
            new ConnectionSessions([new HistoryRetention(history)]),
            new NoProblems());

    private static Task WatchAsync(ContextService service, CancellationToken cancellationToken) =>
        service.RunWatchAsync(
            UserId,
            new WatchContextRequest { ProjectId = ProjectId, WatchId = ShortGuid.NewShortGuid() },
            (_, _) => Task.CompletedTask,
            cancellationToken);

    /// <summary>Discovers nothing for the project root, and fails on the project's own actions.</summary>
    private sealed class ProjectDiscoveryFails(CancellationTokenSource? cancel) : IContextActionResolver
    {
        public async ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
        {
            if (target.Scope != ContextScope.Project)
            {
                return [];
            }

            if (cancel is null)
            {
                throw new InvalidOperationException("The project's actions could not be discovered.");
            }

            await cancel.CancelAsync();
            throw new OperationCanceledException(cancellationToken);
        }

        public ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingHistoryStackStore : IHistoryStackStore
    {
#pragma warning disable CS0067 // The watch never raises it.
        public event EventHandler<HistoryChangedEventArgs>? Changed;
#pragma warning restore CS0067

        public int Retains { get; private set; }

        public int Releases { get; private set; }

        public IHistoryStack Get(string rootPath) => throw new NotSupportedException();

        public void Retain(string rootPath) => Retains++;

        public void Release(string rootPath) => Releases++;
    }

    private sealed class OneProjectStore(ProjectRecord project) : IProjectStore
    {
        public IReadOnlyList<ProjectRecord> List(ShortGuid userId) => [project];

        public ProjectRecord Add(ShortGuid userId, string name, PathRecord path) => throw new NotSupportedException();

        public void Remove(ShortGuid userId, ShortGuid projectId) => throw new NotSupportedException();
    }

    private sealed class NoProperties : IContextPropertyResolver
    {
        public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);

        public ValueTask<ContextPropertyResult> SetAsync(ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoProblems : IContextWatchHooks
    {
        public void ProjectWatched(ProjectRoot root)
        {
        }

        public ProjectProblems CurrentFor(ProjectRoot root) => new();
    }
}
