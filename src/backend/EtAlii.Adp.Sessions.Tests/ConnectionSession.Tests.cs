using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

/// <summary>
/// The scope a connection holds its project and its own parts in: acquired whole or not at all,
/// released in the reverse order and once, and refusing a part that arrives after it was released
/// (code-structure FR-023, FR-067, T192, T193).
/// </summary>
public class ConnectionSessionTests
{
    private static readonly ProjectRoot _root = ProjectRoot.From(Path.Combine(Path.GetTempPath(), "adp-connection-session"));
    private static readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    [Fact]
    public void Open_AcquiresTheProjectParts_InTheOrderGiven()
    {
        // Arrange.
        var log = new List<string>();
        var problems = new ProjectPart("problems", log);

        // Act.
        using var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log), problems]);

        // Assert.
        Assert.Equal(["acquire history", "acquire problems"], log);
        Assert.Equal(_root, problems.AcquiredFor);
        Assert.Equal(_watchId, session.WatchId);
        Assert.Equal(_root, session.Root);
    }

    [Fact]
    public void Dispose_ReleasesWhatWasHeld_ThenTheProject_TheLastAcquiredFirst()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log), new ProjectPart("problems", log)]);
        session.Hold(() => Acquire("hierarchy", log));
        session.Hold(() => Acquire("selection", log));
        log.Clear();

        // Act.
        session.Dispose();

        // Assert.
        Assert.Equal(["release selection", "release hierarchy", "release problems", "release history"], log);
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
            [new ProjectPart("history", log), new FailingPart("problems"), new ProjectPart("never", log)]));

        // Assert.
        Assert.Equal("problems failed", thrown.Message);
        Assert.Equal(["acquire history", "release history"], log);
    }

    [Fact]
    public void Hold_WhenTheAcquisitionFails_HoldsNothingAndTheSessionStillReleasesTheRest()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log)]);
        session.Hold(() => Acquire("hierarchy", log));

        // Act.
        Assert.Throws<InvalidOperationException>(() => session.Hold<IDisposable>(() => throw new InvalidOperationException("selection failed")));
        log.Clear();
        session.Dispose();

        // Assert.
        Assert.Equal(["release hierarchy", "release history"], log);
    }

    [Fact]
    public void Hold_AfterTheSessionWasReleased_ReleasesThePartAtOnceAndThrows()
    {
        // Arrange: a part of the connection that starts while the connection is already going.
        var log = new List<string>();
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log)]);
        session.Dispose();
        log.Clear();

        // Act.
        Assert.Throws<ObjectDisposedException>(() => session.Hold(() => Acquire("selection", log)));

        // Assert.
        Assert.Equal(["acquire selection", "release selection"], log);
    }

    [Fact]
    public void Dispose_CalledTwice_ReleasesOnce()
    {
        // Arrange.
        var log = new List<string>();
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log)]);
        session.Hold(() => Acquire("hierarchy", log));
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
        var session = ConnectionSession.Open(_watchId, _root, [new ProjectPart("history", log)]);
        session.Hold(() => Acquire("hierarchy", log, failsToRelease: true));
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
        var sessions = new ConnectionSessions([new ProjectPart("history", log), new ProjectPart("problems", log)]);

        // Act.
        sessions.Open(_watchId, _root).Dispose();

        // Assert.
        Assert.Equal(["acquire history", "acquire problems", "release problems", "release history"], log);
    }

    private sealed class ProjectPart(string name, List<string> log) : IProjectSessionPart
    {
        public ProjectRoot? AcquiredFor { get; private set; }

        public IDisposable Acquire(ProjectRoot root)
        {
            AcquiredFor = root;
            return ConnectionSessionTests.Acquire(name, log);
        }
    }

    /// <summary>A project part whose acquisition throws.</summary>
    private sealed class FailingPart(string name) : IProjectSessionPart
    {
        public IDisposable Acquire(ProjectRoot root) => throw new InvalidOperationException($"{name} failed");
    }

    private static CallbackDisposable Acquire(string name, List<string> log, bool failsToRelease = false)
    {
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
