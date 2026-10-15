using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Plugins;

/// <summary>
/// What a folder body's reading depends on and when it is read again (FBL §10.3, §11.2): the host
/// notifies changed paths, and the body reads once after the binding's <c>settle</c> delay. Each test
/// runs over a folder on disk, on a clock that moves only when the test moves it.
/// </summary>
public class FolderBodySettleTests
{
    private const string PluginId = "net.example.folder";
    private const int Settle = 250;

    /// <summary>A folder binding of no tool type, as in <see cref="FolderBodyTests"/>, with a settle delay of its own.</summary>
    private static FblBinding Binding()
    {
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""
            { "fbl": "0.1", "bindings": { "notes": {
              "claims": { "names": ["index.txt"] },
              "body": {
                "kind": "folder",
                "recognise": { "all": ["index.txt"] },
                "files": [
                  { "name": "index", "glob": "index.txt", "family": "lines" },
                  { "name": "pages", "glob": "pages/**", "family": "lines" }
                ],
                "ignore": ["pages/**/.hidden"],
                "settle": {{Settle}}
              },
              "reader": { "plugin": "{{PluginId}}", "version": "^0.1.0" }
            } } }
            """), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        return document!.Bindings["notes"];
    }

    private static TemporaryFolder Notes()
    {
        var folder = new TemporaryFolder();
        folder.Write("index.txt", "index");
        folder.Write("pages/a.txt", "a");
        folder.Write("pages/b.txt", "b");
        return folder;
    }

    [Fact]
    public void ABurstOfChangesBecomesOneReadAfterTheSettleDelay()
    {
        // Arrange.
        using var folder = Notes();
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);
        using var body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);
        var changes = new List<FolderBodyChange>();
        body.Changed += (_, change) => changes.Add(change);

        // Act: four changes of three files, each sooner than the delay after the one before.
        foreach (var file in new[] { "pages/a.txt", "pages/b.txt", "index.txt", "pages/a.txt" })
        {
            folder.Write(file, file + " changed");
            body.Notify(Path.Combine(folder.Path, file));
            time.Advance(TimeSpan.FromMilliseconds(100));
        }
        time.Advance(TimeSpan.FromMilliseconds(Settle - 101));
        var readsBeforeTheDelay = plugin.Reads.Count;
        time.Advance(TimeSpan.FromMilliseconds(1));

        // Assert: the opening read, then one more, once.
        Assert.Equal(1, readsBeforeTheDelay);
        Assert.Equal(2, plugin.Reads.Count);
        Assert.Equal("pages/a.txt changed", Encoding.UTF8.GetString(plugin.Reads[1].Files.Single(f => f.RelativePath == "pages/a.txt").Bytes));
        Assert.Equal([(true, false)], changes.Select(change => (change.FolderRead, change.Registration)));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, plugin.Reads.Count);
        Assert.Equal(0, time.Pending);
    }

    [Fact]
    public void AChangeOutsideEveryDependencyCausesNoRead()
    {
        // Arrange.
        using var folder = Notes();
        using var outside = new TemporaryFolder();
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);
        using var body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);
        var changes = new List<FolderBodyChange>();
        body.Changed += (_, change) => changes.Add(change);

        // Act: a file no rule selects, a folder no glob covers, an ignored file, and a file elsewhere.
        body.Notify(folder.Write("notes.md", "x"));
        body.Notify(folder.Write("other/c.txt", "x"));
        body.Notify(folder.Write("pages/b/.hidden", "x"));
        body.Notify(outside.Write("pages/a.txt", "x"));
        time.Advance(TimeSpan.FromSeconds(10));

        // Assert.
        Assert.Single(plugin.Reads);
        Assert.Empty(changes);
        Assert.Equal(0, time.Pending);
        Assert.DoesNotContain(body.Dependencies, path => path.StartsWith(Path.Combine(folder.Path, "other"), StringComparison.Ordinal));
    }

    [Fact]
    public void ANewFileInAFolderAGlobCoversCausesOneRead()
    {
        // Arrange: pages/deep exists and holds no file yet.
        using var folder = Notes();
        Directory.CreateDirectory(Path.Combine(folder.Path, "pages", "deep"));
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);
        using var body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);

        // Act.
        var dependencies = body.Dependencies;
        body.Notify(folder.Write("pages/deep/new.txt", "new"));
        time.Advance(TimeSpan.FromMilliseconds(Settle));

        // Assert: the folder was named for the host to watch, and the new file is read.
        Assert.Equal(
            [folder.Path, Path.Combine(folder.Path, "index.txt"), Path.Combine(folder.Path, "pages"), Path.Combine(folder.Path, "pages", "a.txt"), Path.Combine(folder.Path, "pages", "b.txt"), Path.Combine(folder.Path, "pages", "deep")],
            dependencies);
        Assert.Equal(2, plugin.Reads.Count);
        Assert.Contains("pages/deep/new.txt", plugin.Reads[1].Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void WhatThePluginWatchesIsADependency()
    {
        // Arrange: the plugin's reading depends on a file outside the folder.
        using var folder = Notes();
        using var shared = new TemporaryFolder();
        var watched = shared.Write("shared.txt", "shared");
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId) { Watched = [watched] };
        using var body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);

        // Act.
        body.Notify(watched);
        time.Advance(TimeSpan.FromMilliseconds(Settle));

        // Assert.
        Assert.Contains(watched, body.Dependencies);
        Assert.Equal(2, plugin.Reads.Count);
    }

    [Fact]
    public void AChangeOfOnlyTheRegistrationRaisesTheChangeWithoutARead()
    {
        // Arrange: the registration sits in the folder, and is no file rule's file.
        using var folder = Notes();
        var registration = Path.Combine(folder.Path, "notes.adp");
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);
        using var body = FolderBody.Open(folder.Path, Binding(), plugin, registration: registration, time: time);
        var first = new List<FolderBodyChange>();
        var second = new List<FolderBodyChange>();
        body.Changed += (_, change) => first.Add(change);
        body.Changed += (_, change) => second.Add(change);

        // Act: a position written twice, as a placement and its correction.
        folder.Write("notes.adp", "position 1");
        body.Notify(registration);
        folder.Write("notes.adp", "position 2");
        body.Notify(registration);
        time.Advance(TimeSpan.FromMilliseconds(Settle));

        // Assert: every open reading is told, and the folder is not read again.
        Assert.Contains(registration, body.Dependencies);
        Assert.Single(plugin.Reads);
        Assert.Equal([(false, true)], first.Select(change => (change.FolderRead, change.Registration)));
        Assert.Equal(first, second);
    }

    [Fact]
    public void AOneShotReadOrAClosedBodyLeavesNothingPending()
    {
        // Arrange.
        using var folder = Notes();
        var time = new ManualTime();
        var plugin = new RecordingFolderPlugin(PluginId);

        // Act: a one-shot read, and an open body closed while a change waits.
        var model = FolderBody.ReadOnce(folder.Path, Binding(), plugin);
        var body = FolderBody.Open(folder.Path, Binding(), plugin, time: time);
        var changes = new List<FolderBodyChange>();
        body.Changed += (_, change) => changes.Add(change);
        body.Notify(Path.Combine(folder.Path, "pages", "a.txt"));
        body.Dispose();
        body.Notify(Path.Combine(folder.Path, "pages", "b.txt"));
        time.Advance(TimeSpan.FromSeconds(10));

        // Assert.
        Assert.Equal(3, model.Elements.Count);
        Assert.Equal(2, plugin.Reads.Count);
        Assert.Empty(changes);
        Assert.Equal(0, time.Pending);
    }
}

/// <summary>A clock that moves only when <see cref="Advance"/> moves it, firing every timer that falls due on the way.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The timers due to fire: neither disposed nor stopped.</summary>
    public int Pending => _timers.Count(timer => timer.Due is not null);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        while (_timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } next)
        {
            _now = next.Due!.Value;
            next.Due = null;
            next.Fire();
        }
        _now = end;
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
            return true;
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
