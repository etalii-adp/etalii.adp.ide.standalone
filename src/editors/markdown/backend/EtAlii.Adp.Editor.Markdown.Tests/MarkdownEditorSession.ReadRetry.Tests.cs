using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Markdown.Tests;

/// <summary>
/// The re-read behind an external change: a refusal must not consume the notification.
/// </summary>
/// <remarks>
/// <para>
/// This session re-read ONCE while a comment beside its watcher said the re-read retried - a
/// sentence copied from <c>PlainEditorSession</c>, where it was true. A re-read refused on a
/// write's last event therefore lost that change for good, the defect traced in c4's store as the
/// EditorResolution 60-second flake.
/// </para>
/// <para>
/// <b>Driven through the session, not the helper.</b> Plain's guard exercises
/// <c>ReadWithRetry</c> directly, which stays green if the session stops calling it. These call
/// <c>OnExternalChange</c> as the watcher would, with an open that refuses exactly as often as the
/// test says. The re-read opens a DIFFERENT file from the one watched, so nothing on the watched
/// name changes and no watcher event can race the test for its scripted refusal.
/// </para>
/// </remarks>
public class MarkdownEditorSessionReadRetryTests : IDisposable
{
    private const string Before = "# Before\r\n";
    private const string After = "# After\r\n";

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));

    public MarkdownEditorSessionReadRetryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnExternalChangeWhoseReReadIsRefusedOnce_StillDeliversTheChange()
    {
        // Arrange: a session open on the file, and its next re-read scripted to be refused once.
        var opener = new ScriptedOpen(Written("readme.md", Before), Written("after.md", After));
        await using var session = new MarkdownEditorSession(opener.Watched, opener.Open);
        Assert.Equal(Before, session.Content);
        string? told = null;
        session.Changed += (_, args) => told = args.Content;
        opener.Refusals = 1;

        // Act: what the watcher does on the write's last event.
        session.OnExternalChange();

        // Assert: the refusal was hit - not the file read some other way.
        Assert.Equal(1, opener.Refused);
        Assert.Equal(0, opener.Refusals);

        // Assert: the change arrived. Before the retry this is where it failed: logged, returned,
        // and nothing ever re-read.
        Assert.Equal(After, told);
        Assert.Equal(After, session.Content);
        Assert.Equal("", session.Refusal);
    }

    [Fact]
    public async Task AReReadRefusedOnEveryAttempt_KeepsTheLastGoodContent_OnlyOnceTheAttemptsAreSpent()
    {
        var opener = new ScriptedOpen(Written("readme.md", Before), Written("after.md", After));
        await using var session = new MarkdownEditorSession(opener.Watched, opener.Open);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        opener.Refusals = int.MaxValue;
        var readsBefore = opener.Reads;

        session.OnExternalChange();

        // Three attempts, PlainEditorSession's figure: a retry that gave up at once and one that
        // gave up after three leave the same content, so only the count tells them apart.
        Assert.Equal(3, opener.Reads - readsBefore);
        Assert.Equal(0, changes);
        Assert.Equal(Before, session.Content);
        Assert.NotEqual("", session.Refusal);
    }

    private string Written(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Opens the watched file for the session's first read, and afterwards refuses as often as
    /// scripted before opening the file holding the changed content.
    /// </summary>
    private sealed class ScriptedOpen(string watched, string changed)
    {
        public string Watched { get; } = watched;

        public int Refusals { get; set; }

        public int Refused { get; private set; }

        public int Reads { get; private set; }

        public TextFileBufferOpenResult Open(string path)
        {
            Reads++;
            if (Reads == 1)
            {
                return TextFileBuffer.Open(path);
            }

            if (Refusals > 0)
            {
                Refusals--;
                Refused++;
                return TextFileBufferOpenResult.Refused($"'{IoPath.GetFileName(path)}' does not exist.");
            }

            return TextFileBuffer.Open(changed);
        }
    }
}
