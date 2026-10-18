using System.Collections.Concurrent;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The presence poll can find the root gone just as its watch closes. The watch waits for the
/// poll, and for the recovery its announcement starts, before it returns and disposes the
/// recovery source (code-structure FR-070): neither outlives the watch, and the recovery, handed a
/// cancelled token, simply gives up rather than faulting in a task nobody observes (found by the
/// Rider warnings cleanup, pull request 5, where an AccessToDisposedClosure suppression claimed
/// the opposite).
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
    public async Task RootFoundMissingAsTheWatchCloses_TheWatchWaitsForThePollAndTheRecoveryItStarts()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = ShortGuid.NewShortGuid();
        var projectStore = new FileProjectStore(_appDataRoot, new FileWriter());
        var project = await projectStore.AddAsync(
            userId,
            "late-recovery",
            new PathRecord(_projectFolder.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)),
            cancellationToken);
        using var modelStore = new HierarchyModelStore(TestToolRouters.For());
        var watchId = ShortGuid.NewShortGuid();

        // The watch takes this same model from the store; listening on it shows when the late
        // announcement has run, because the recovery starts right after it on the same thread.
        var model = modelStore.GetOrCreate(watchId, ProjectRoot.From(_projectFolder));
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
        using var events = new ProjectFileEvents();
        var clock = new FakeTimeProvider();
        var watcher = new HierarchyWatch(modelStore, events, clock)
        {
            RootFoundMissing = async () =>
            {
                foundMissing.TrySetResult();
                await release.Task;
            },
        };
        var service = new HierarchyService(projectStore, modelStore, new ConnectionSessions([]), watcher);

        // A fault in a task nobody awaits surfaces only as an unobserved task exception once the
        // task is collected.
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

            // Act: the poll finds the root gone and is held there while the watch is closed.
            Directory.Delete(_projectFolder);
            var polled = Task.WhenAny(foundMissing.Task, watch);
            await clock.DriveAsync(polled, HierarchyWatch.RootRecoveryPollInterval, steps: 200);
            if (await polled == watch)
            {
                // The watch ended before the poll got there: surface why, rather than a timeout.
                await watch;
                Assert.Fail("The watch ended before its presence poll found the root gone.");
            }

            await watchCts.CancelAsync();

            // Assert: the watch is still closing while its poll is held, and returns once the poll
            // has announced the outage and the recovery that started has given up.
            var first = await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            Assert.False(first == watch, "The watch returned while its presence poll was still running.");
            release.SetResult();
            await watch.WaitAsync(StepTimeout, cancellationToken);
            Assert.True(announced.Task.IsCompleted, "The watch returned before its poll announced the outage.");

            // And nothing faulted unobserved: a fault is only reported once its task is
            // finalized, so the collector is asked to run at every look.
            await Eventually.TrueAsync(
                () =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    return !faults.IsEmpty;
                },
                cancellationToken,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(50));

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
                .Where(exception => exception.StackTrace?.Contains(nameof(HierarchyWatch), StringComparison.Ordinal) == true)
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
