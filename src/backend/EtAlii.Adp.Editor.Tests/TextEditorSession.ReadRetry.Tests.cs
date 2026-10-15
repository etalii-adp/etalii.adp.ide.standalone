using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// The re-read retry behind <c>OnExternalChange</c>: a refusal must not consume the notification.
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
/// passes by luck is not a guard - so the retry takes its reader as a function and these tests
/// supply one that refuses exactly as often as they choose.
/// </para>
/// <para>
/// <b>Seen to fail:</b> against a single-attempt read - <c>attempts: 1</c>, which is what the code
/// did before - the transient case returns the refusal and the first test reddens, while the
/// permanent case and the attempt count are unaffected. That is the discrimination: one test alone
/// would pass on a retry that never gave up, and another alone on one that never retried.
/// </para>
/// <para>
/// <b>And driven through the session, not only the helper.</b> A guard of <c>ReadWithRetry</c>
/// alone stays green if the session stops calling it - which is how the markdown editor's copy of
/// this session came to re-read once while a comment beside its watcher said the re-read retried.
/// The session cases call <c>OnExternalChange</c> as the watcher would, with an open that refuses
/// exactly as often as the test says. The re-read opens a DIFFERENT file from the one watched, so
/// nothing on the watched name changes and no watcher event can race the test for its scripted
/// refusal.
/// </para>
/// </remarks>
public class TextEditorSessionReadRetryTests : IDisposable
{
    private const string Before = "before\r\n";
    private const string After = "after\r\n";

    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    [Fact]
    public void ATransientRefusalIsRetriedRatherThanConsumed()
    {
        var refusalsLeft = 2;
        var result = TextEditorSession.ReadWithRetry(
            () => refusalsLeft-- > 0
                ? TextFileBufferOpenResult.Refused("'x' does not exist.")
                : TextFileBuffer.Open(WrittenFile("after the replace landed")),
            attempts: 3,
            NoWait);

        Assert.NotNull(result.Buffer);
        Assert.Equal("after the replace landed", result.Buffer!.Content);
        Assert.Equal("", result.Refusal);
    }

    [Fact]
    public void APermanentRefusalIsStillReportedRatherThanRetriedForever()
    {
        var reads = 0;
        var result = TextEditorSession.ReadWithRetry(
            () =>
            {
                reads++;
                return TextFileBufferOpenResult.Refused("'x' does not exist.");
            },
            attempts: 3,
            NoWait);

        Assert.Null(result.Buffer);
        Assert.Equal("'x' does not exist.", result.Refusal);

        // THE ATTEMPT COUNT IS THE ASSERTION, NOT THE OUTCOME, AND IT MUST NOT BE TRIMMED AS AN
        // IMPLEMENTATION DETAIL. A retry that gave up immediately and one that gave up after three
        // produce the IDENTICAL refusal, so the outcome cannot tell them apart and only the count
        // can. That is the same wrong-dimension trap this repository ruled on in SolutionWatcher's
        // tests the same morning - an assertion true of the wrong property of the right subject.
        Assert.Equal(3, reads);
    }

    [Fact]
    public void ASucceedingReadIsNotRetried()
    {
        var reads = 0;
        var path = WrittenFile("first time");

        var result = TextEditorSession.ReadWithRetry(
            () =>
            {
                reads++;
                return TextFileBuffer.Open(path);
            },
            attempts: 3,
            NoWait);

        Assert.NotNull(result.Buffer);
        Assert.Equal(1, reads);
    }

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
        await using var session = new TextEditorSession(opener.Watched, opener.Open);
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
        var opener = new ScriptedOpen(Written("notes.txt", Before), Written("after.txt", After));
        await using var session = new TextEditorSession(opener.Watched, opener.Open);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        opener.Refusals = int.MaxValue;
        var readsBefore = opener.Reads;

        session.OnExternalChange();

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

    private static string WrittenFile(string content)
    {
        var path = IoPath.Combine(IoPath.GetTempPath(), $"adp-retry-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
