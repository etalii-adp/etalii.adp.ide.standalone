using System.Collections.Concurrent;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The presence poll can find the root gone just as its watch closes: <c>CancelAsync</c> in the
/// watch's <c>finally</c> asks the poll to stop but does not wait for it, so a poll already past
/// its delay announces the outage after the recovery source is disposed. That late announcement
/// must start a recovery that simply gives up, not one that faults on the disposed source in a
/// task nobody observes (found by the Rider warnings cleanup, pull request 5, where an
/// AccessToDisposedClosure suppression claimed the opposite).
/// </summary>
public class HierarchyServiceLateRecoveryTests : IDisposable
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    private readonly string _appDataRoot;
    private readonly string _projectFolder;

    public HierarchyServiceLateRecoveryTests()
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "late-recovery");
        Directory.CreateDirectory(_projectFolder);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task RootFoundMissingAsTheWatchCloses_StartsARecoveryThatDoesNotFaultOnTheDisposedSource()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = ShortGuid.NewShortGuid();
        var projectStore = new FileProjectStore(_appDataRoot);
        var project = projectStore.Add(
            userId,
            "late-recovery",
            new PathRecord(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)));
        using var modelStore = new HierarchyModelStore();
        var watchId = ShortGuid.NewShortGuid();

        // The watch takes this same model from the store; listening on it shows when the late
        // announcement has run, because the recovery starts right after it on the same thread.
        var model = modelStore.GetOrCreate(watchId, _projectFolder);
        var announced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.EntryChanged += change =>
        {
            if (change is HierarchyRootUnavailable)
            {
                announced.TrySetResult();
            }
        };

        var foundMissing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new HierarchyService(projectStore, modelStore)
        {
            RootFoundMissing = async () =>
            {
                foundMissing.TrySetResult();
                await release.Task;
            },
        };

        // The recovery task is discarded where it starts, so a fault in it surfaces only as an
        // unobserved task exception once the task is collected.
        var faults = new ConcurrentQueue<Exception>();

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var watch = service.RunWatchAsync(
                userId,
                new WatchHierarchyRequest { ProjectId = project.Id, WatchId = watchId },
                (_, _) => Task.CompletedTask,
                watchCts.Token);

            // Act: the poll finds the root gone and is held there while the watch closes and
            // disposes its recovery source; only then does the poll announce the outage.
            Directory.Delete(_projectFolder);
            if (await Task.WhenAny(foundMissing.Task, watch).WaitAsync(StepTimeout, cancellationToken) == watch)
            {
                // The watch ended before the poll got there: surface why, rather than a timeout.
                await watch;
                Assert.Fail("The watch ended before its presence poll found the root gone.");
            }

            await watchCts.CancelAsync();
            await watch.WaitAsync(StepTimeout, cancellationToken);
            release.SetResult();
            await announced.Task.WaitAsync(StepTimeout, cancellationToken);

            // Assert.
            for (var attempt = 0; attempt < 20 && faults.IsEmpty; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            Assert.True(
                faults.IsEmpty,
                "The late recovery faulted, unobserved: " + string.Join(Environment.NewLine, faults.Select(fault => fault.ToString())));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        return;

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            var ours = args.Exception.Flatten().InnerExceptions
                .Where(exception => exception.StackTrace?.Contains(nameof(HierarchyService), StringComparison.Ordinal) == true)
                .ToList();
            if (ours.Count == 0)
            {
                return;
            }

            ours.ForEach(faults.Enqueue);
            args.SetObserved();
        }
    }
}
