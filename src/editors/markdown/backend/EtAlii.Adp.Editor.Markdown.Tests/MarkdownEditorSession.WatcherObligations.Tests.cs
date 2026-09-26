using System.Reflection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Markdown.Tests;

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
/// <c>FileSystemWatcher.OnError</c>. That raises synchronously, so the case is deterministic rather
/// than patient.
/// </para>
/// </remarks>
public class MarkdownEditorSessionWatcherObligationsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-markdown-obligations-").FullName;

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ADeletion_IsLearned()
    {
        var path = Write("notes.md", "content");
        await using var session = new MarkdownEditorSession(path);
        Assert.Equal("", session.Refusal);

        File.Delete(path);

        // Learned means the session stops claiming the file opens and says why, while keeping the last
        // good content. A real watcher, so this waits - passing the moment the session hears.
        Assert.True(await Eventually(() => session.Refusal.Length > 0), "The session never learned its file was deleted.");
        Assert.Equal("content", session.Content);
    }

    [Fact]
    public async Task ALostEventsWindow_IsLearned_ByReadingTheFileAgain()
    {
        var path = Write("notes.md", "before");
        await using var session = new MarkdownEditorSession(path);
        var watcher = WatcherOf(session);
        string? delivered = null;
        session.Changed += (_, args) => delivered = args.Content;

        // Lose the change: nothing is watching while it happens, so no event can carry it.
        watcher.EnableRaisingEvents = false;
        await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);

        RaiseOverflow(watcher);

        Assert.Equal("after", delivered);
        Assert.Equal("after", session.Content);
    }

    private static FileSystemWatcher WatcherOf(MarkdownEditorSession session) =>
        typeof(MarkdownEditorSession).GetField("_watcher", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session) as FileSystemWatcher
        ?? throw new InvalidOperationException("MarkdownEditorSession no longer keeps its watcher in _watcher; this test must follow it.");

    // The one signal a watcher gives when it has dropped events, raised the way the watcher raises it.
    private static void RaiseOverflow(FileSystemWatcher watcher) =>
        (typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FileSystemWatcher.OnError was not found."))
        .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException())]);

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return true;
    }

    private string Write(string name, string text)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }
}
