using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

/// <summary>
/// The scope a connection holds its project and its own parts in: acquired whole or not at all,
/// released in the reverse order and once (code-structure FR-023, FR-067, T192).
/// </summary>
public class ConnectionSessionTests
{
    private static readonly ProjectRoot _root = ProjectRoot.From(Path.Combine(Path.GetTempPath(), "adp-connection-session"));
    private static readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    [Fact]
    public void Open_AcquiresTheProjectPartsThenTheConnectionParts_InTheOrderGiven()
    {
        // Arrange.
        var log = new List<string>();

        // Act.
        using var session = ConnectionSession.Open(
            _watchId,
            _root,
            [new ProjectPart("history", log), new ProjectPart("problems", log)],
            [new ConnectionPart("hierarchy", log)]);

        // Assert.
        Assert.Equal(["acquire history", "acquire problems", "acquire hierarchy"], log);
        Assert.Equal(_watchId, session.WatchId);
        Assert.Equal(_root, session.Root);
    }

    [Fact]
    public void Dispose_ReleasesEveryPart_TheLastAcquiredFirst()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(
            _watchId,
            _root,
            [new ProjectPart("history", log), new ProjectPart("problems", log)],
            [new ConnectionPart("hierarchy", log)]);
        log.Clear();

        // Act.
        session.Dispose();

        // Assert.
        Assert.Equal(["release hierarchy", "release problems", "release history"], log);
    }

    [Fact]
    public void Open_WhenAProjectPartFails_ReleasesWhatWasAcquiredAndAcquiresNothingMore()
    {
        // Arrange: the watch whose first discovery failed after its history was retained (FR-023).
        var log = new List<string>();

        // Act.
        var thrown = Assert.Throws<InvalidOperationException>(() => ConnectionSession.Open(
            _watchId,
            _root,
            [new ProjectPart("history", log), new ProjectPart("problems", log, failsToAcquire: true)],
            [new ConnectionPart("hierarchy", log)]));

        // Assert.
        Assert.Equal("problems failed", thrown.Message);
        Assert.Equal(["acquire history", "release history"], log);
    }

    [Fact]
    public void Open_WhenAConnectionPartFails_ReleasesItsProjectSessionToo()
    {
        // Arrange.
        var log = new List<string>();

        // Act.
        var thrown = Assert.Throws<InvalidOperationException>(() => ConnectionSession.Open(
            _watchId,
            _root,
            [new ProjectPart("history", log)],
            [new ConnectionPart("hierarchy", log), new ConnectionPart("selection", log, failsToAcquire: true)]));

        // Assert.
        Assert.Equal("selection failed", thrown.Message);
        Assert.Equal(["acquire history", "acquire hierarchy", "release hierarchy", "release history"], log);
    }

    [Fact]
    public void Dispose_CalledTwice_ReleasesOnce()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log)], [new ConnectionPart("hierarchy", log)]);
        session.Dispose();
        log.Clear();

        // Act.
        session.Dispose();

        // Assert.
        Assert.Empty(log);
    }

    [Fact]
    public void Dispose_WhenAReleaseFails_StillReleasesTheRest_ThenThrows()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(
            _watchId,
            _root,
            [new ProjectPart("history", log)],
            [new ConnectionPart("hierarchy", log, failsToRelease: true)]);
        log.Clear();

        // Act.
        var thrown = Assert.Throws<InvalidOperationException>(session.Dispose);

        // Assert.
        Assert.Equal("hierarchy failed to release", thrown.Message);
        Assert.Equal(["release hierarchy", "release history"], log);
    }

    [Fact]
    public void ConnectionSessions_Open_UsesTheRegisteredParts()
    {
        // Arrange.
        var log = new List<string>();
        var sessions = new ConnectionSessions([new ProjectPart("history", log)], [new ConnectionPart("hierarchy", log)]);

        // Act.
        sessions.Open(_watchId, _root).Dispose();

        // Assert.
        Assert.Equal(["acquire history", "acquire hierarchy", "release hierarchy", "release history"], log);
    }

    private sealed class ProjectPart(string name, List<string> log, bool failsToAcquire = false) : IProjectSessionPart
    {
        public IDisposable Acquire(ProjectRoot root) => Part.Acquire(name, log, failsToAcquire, failsToRelease: false);
    }

    private sealed class ConnectionPart(string name, List<string> log, bool failsToAcquire = false, bool failsToRelease = false) : IConnectionSessionPart
    {
        public IDisposable Acquire(ShortGuid watchId, ProjectRoot root) => Part.Acquire(name, log, failsToAcquire, failsToRelease);
    }

    private static class Part
    {
        public static IDisposable Acquire(string name, List<string> log, bool failsToAcquire, bool failsToRelease)
        {
            if (failsToAcquire)
            {
                throw new InvalidOperationException($"{name} failed");
            }

            log.Add($"acquire {name}");
            return new CallbackDisposable(() =>
            {
                log.Add($"release {name}");
                if (failsToRelease)
                {
                    throw new InvalidOperationException($"{name} failed to release");
                }
            });
        }
    }
}
