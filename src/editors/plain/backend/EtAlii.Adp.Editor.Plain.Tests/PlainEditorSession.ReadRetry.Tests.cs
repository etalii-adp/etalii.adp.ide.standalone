using Xunit;

namespace EtAlii.Adp.Editor.Plain.Tests;

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
/// </remarks>
public class PlainEditorSessionReadRetryTests
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    [Fact]
    public void ATransientRefusalIsRetriedRatherThanConsumed()
    {
        var refusalsLeft = 2;
        var result = PlainEditorSession.ReadWithRetry(
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
        var result = PlainEditorSession.ReadWithRetry(
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

        var result = PlainEditorSession.ReadWithRetry(
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

    private static string WrittenFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"adp-retry-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
