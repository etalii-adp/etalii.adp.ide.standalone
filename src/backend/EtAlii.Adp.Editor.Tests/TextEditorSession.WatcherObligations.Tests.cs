using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// The session learns of every change the shared writer can make to its file - here a deletion and a
/// window in which events were lost - rather than merely subscribing to the events that carry them
/// (backend-centralization R2.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>The obligation, not the event set.</b> WatcherWiring's sweep checks that a watcher SUBSCRIBES to
/// Error, and this session always did - while its handler only logged, so an overflow left the editor
/// showing what it held before. The sweep called that compliant. These tests ask what the session
/// would miss, which is the question R2.9 says a reviewer asks.
/// </para>
/// <para>
/// <b>How each case is caused.</b> A deletion is a real delete against the real watcher, so a writer
/// that removes a file by some other means fails here rather than passing by habit. A lost-events
/// window cannot be caused on demand, so the watcher is switched off while the file changes - no event
/// can carry that change - and then its own <c>Error</c> is raised through
/// <c>FileSystemWatcher.OnError</c>. That raises synchronously; the re-read it asks for runs on the
/// pool, so the case waits for it, and nothing but the overflow can have asked.
/// </para>
/// </remarks>
public class TextEditorSessionWatcherObligationsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-text-obligations-").FullName;

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ADeletion_IsLearned()
    {
        var path = Write("notes.txt", "content");
        await using var session = new TextEditorSession(path);
        Assert.Equal("", session.Refusal);

        File.Delete(path);

        // Learned means the session stops claiming the file opens and says why, while keeping the last
        // good content. A real watcher, so this waits - passing the moment the session hears.
        Assert.True(await Eventually.TrueAsync(() => session.Refusal.Length > 0, TestContext.Current.CancellationToken, Patience), "The session never learned its file was deleted.");
        Assert.Equal("content", session.Content);
    }

    [Fact]
    public async Task ADeletion_IsLearned_ThroughTheProjectsOneWatcher()
    {
        // Arrange: the project root is watched already, as the hierarchy watches an open project, so
        // the session hears its file through that watcher and makes none of its own (code-structure T280).
        using var events = new ProjectFileEvents();
        using var project = events.Subscribe(_root, _ => { });
        var path = Write("notes.txt", "content");
        await using var session = new TextEditorSession(path, events);
        Assert.False(session.Watched!.HasWatcherOfItsOwn());
        Assert.Equal(1, events.WatchedRoots());

        // Act.
        File.Delete(path);

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => session.Refusal.Length > 0, TestContext.Current.CancellationToken, Patience), "The session never learned its file was deleted.");
        Assert.Equal("content", session.Content);
    }

    [Fact]
    public async Task ALostEventsWindow_IsLearned_ByReadingTheFileAgain()
    {
        var path = Write("notes.txt", "before");
        await using var session = new TextEditorSession(path);
        var watcher = session.Watched!.Watcher();
        string? delivered = null;
        session.Changed += (_, args) => delivered = args.Content;

        // Lose the change: nothing is watching while it happens, so no event can carry it.
        watcher.EnableRaisingEvents = false;
        await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);

        watcher.RaiseOverflow();

        // The re-read runs off the watcher's thread, one at a time, so the overflow is answered
        // shortly after rather than during the raise.
        Assert.True(await Eventually.TrueAsync(() => delivered == "after", TestContext.Current.CancellationToken, Patience), "The overflow never led to a re-read.");
        Assert.Equal("after", session.Content);
    }

    // The one signal a watcher gives when it has dropped events, raised the way the watcher raises it.

    private string Write(string name, string text)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }
}
