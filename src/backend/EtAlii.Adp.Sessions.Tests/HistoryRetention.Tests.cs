using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

/// <summary>The project's history as a part of a project session (code-structure FR-067, T192).</summary>
public class HistoryRetentionTests
{
    [Fact]
    public void Acquire_RetainsTheProjectsStack_AndReleasingItReleasesItOnce()
    {
        // Arrange.
        var root = ProjectRoot.From(Path.Combine(Path.GetTempPath(), "adp-history-retention"));
        var stacks = new RecordingStacks();
        var retention = new HistoryRetention(stacks);

        // Act.
        var held = retention.Acquire(root);
        var retainedBeforeRelease = stacks.Calls.ToList();
        held.Dispose();
        held.Dispose();

        // Assert.
        Assert.Equal([$"retain {root.FullPath}"], retainedBeforeRelease);
        Assert.Equal([$"retain {root.FullPath}", $"release {root.FullPath}"], stacks.Calls);
    }

    private sealed class RecordingStacks : IHistoryStackStore
    {
        public List<string> Calls { get; } = [];

        public event EventHandler<HistoryChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public IHistoryStack Get(string rootPath) => throw new NotSupportedException();

        public void Retain(string rootPath) => Calls.Add($"retain {rootPath}");

        public void Release(string rootPath) => Calls.Add($"release {rootPath}");
    }
}
