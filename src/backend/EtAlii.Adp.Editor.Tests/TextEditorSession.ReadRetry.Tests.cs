using EtAlii.Adp.Files;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// The re-read retry behind <c>OnExternalChangeAsync</c>: a refusal must not consume the notification.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it protects.</b> A publish through <c>AdpFileWriter</c> is a temp-then-replace, and
/// <c>TextFileBuffer.Open</c> refuses on <c>!info.Exists</c> <b>before any exception handling</b>.
/// A callback landing while the destination name is in transit therefore got "does not exist", and
/// the code logged it and returned - consuming the only notification for that write. A reader
/// waiting on the resulting <c>Changed</c> event then waited for something already delivered and
/// discarded, and the wait ended only at whatever deadline was above it.
/// </para>
/// <para>
/// <b>Why a seam rather than the real file system.</b> The window is microseconds wide by design.
/// A test that deleted and recreated a file could only land inside it by luck, and a guard that
/// passes by luck is not a guard - so the session takes its open as a function and these tests
/// supply one that refuses exactly as often as they choose. The retry waits on a fake clock, which
/// the tests move; <c>WatchedFileTests</c> guard the retry itself.
/// </para>
/// <para>
/// <b>Seen to fail:</b> against a single-attempt read, which is what the code did before, the
/// transient case loses the change and the permanent case counts one read rather than three. One
/// test alone would pass on a retry that never gave up, and the other alone on one that never
/// retried.
/// </para>
/// <para>
/// <b>Driven through the session, not only the helper.</b> A guard of the retry alone stays green
/// if the session stops reading through it - which is how the markdown editor's copy of this session
/// came to re-read once while a comment beside its watcher said the re-read retried. These cases
/// call <c>OnExternalChangeAsync</c> as the watched file would, with an open that refuses
/// exactly as often as the test says. The re-read opens a DIFFERENT file from the one watched, so
/// nothing on the watched name changes and no watcher event can race the test for its scripted
/// refusal.
/// </para>
/// </remarks>
public class TextEditorSessionReadRetryTests : IDisposable
{
    private const string Before = "before\r\n";
    private const string After = "after\r\n";

    private readonly FakeTimeProvider _clock = new();

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));

    public TextEditorSessionReadRetryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnExternalChangeWhoseReReadIsRefusedOnce_StillDeliversTheChange()
    {
        // Arrange: a session open on the file, and its next re-read scripted to be refused once.
        var opener = new ScriptedOpen(Written("notes.txt", Before), Written("after.txt", After));
        await using var session = new TextEditorSession(opener.Watched, opener.Open, _clock);
        Assert.Equal(Before, session.Content);
        string? told = null;
        session.Changed += (_, args) => told = args.Content;
        opener.Refusals = 1;

        // Act: what the watcher does on the write's last event.
        await _clock.DriveAsync(session.OnExternalChangeAsync(), WatchedFile.BetweenReadAttempts);

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
        var opener = new ScriptedOpen(Written("notes.txt", Before), Written("after.txt", After));
        await using var session = new TextEditorSession(opener.Watched, opener.Open, _clock);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        opener.Refusals = int.MaxValue;
        var readsBefore = opener.Reads;

        await _clock.DriveAsync(session.OnExternalChangeAsync(), WatchedFile.BetweenReadAttempts);

        // Three attempts: a retry that gave up at once and one that
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
