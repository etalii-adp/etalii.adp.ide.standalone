using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// What a reader must permit for <see cref="AdpFileWriter.Save"/> to be able to replace the file
/// underneath it — the contract behind a flaky integration failure, pinned deterministically.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this explains.</b> Under concurrent load a flow test failed with
/// <c>plan.tml could not be written</c>, and the stack said
/// <c>UnauthorizedAccessException</c> out of <c>File.Move(..., overwrite: true)</c> inside
/// <c>Save</c>. A temp-then-move publish does not merely write the destination, it *replaces*
/// it — and Windows permits that only if every open handle on the destination was opened
/// sharing <c>Delete</c>. A reader sharing only <c>Read</c> is enough to deny it.
/// </para>
/// <para>
/// <b>Which is not a defect in the writer.</b> Every read in the application goes through
/// <see cref="SharedDocumentReader"/>, which shares <c>ReadWrite | Delete</c> precisely so a
/// publish can land mid-read; the <c>ShapeOfFileAccess</c> guard is what keeps that true. The
/// handles that do not are in test code, which that guard deliberately does not police. So a
/// test that reads a document with <c>File.ReadAllText</c> and then triggers a save is holding
/// the file in a way no application reader ever does, and is racing its own subject.
/// </para>
/// <para>
/// These two facts are asserted rather than reasoned about because the integration symptom
/// reproduces roughly once in twenty concurrent runs — a rate at which a green suite says
/// nothing. Here the same mechanism is deterministic in both directions.
/// </para>
/// <para>
/// <b>The mechanism map lives with the decision, not here.</b> Three write primitives against two
/// sharing modes are tabulated in <c>AdpFileWriter.Save</c>, beside the line that chooses between
/// them — which is where anyone reaching for <c>File.Move(overwrite: true)</c> is standing. It is
/// not repeated in this file, because one measured fact with two homes is one fact that can drift.
/// </para>
/// <para>
/// <b>What the investigation eliminated, so nobody pays for it twice.</b> The symptom was
/// <c>OwlFlowTests.AClassReposition…</c> failing about one concurrent run in twenty. Two
/// plausible suspects were tested and cleared, and both cost more to re-derive than to record:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>The tests' own read sharing.</b> A test reading a document with <c>File.ReadAllText</c>
/// holds it sharing <c>Read</c>, which does deny a replace — a real mechanism. Converting 48 such
/// reads across 11 flow-test files changed nothing measurable, so the change was reverted rather
/// than kept on the strength of a plausible story. It was not the cause.
/// </description></item>
/// <item><description>
/// <b>The environment.</b> Antivirus or the search indexer transiently holding files under
/// <c>%TEMP%</c> fitted the wandering pattern and was the leading theory for a while. It was
/// wrong: the holder was ADP itself — <c>RegistrationLayout</c>'s registration read, which shared
/// <c>Read</c> only and so denied the registration write that followed it.
/// </description></item>
/// </list>
/// <para>
/// <b>And a caveat about how any of this was reproduced.</b> The harness was four test hosts
/// running the integration assembly concurrently against one <c>%TEMP%</c> root. That is harsher
/// than the real gate, which runs one host, so a failure it produces is not automatically a defect
/// — teardown races on the problem cache showed up there and are artefacts of the harness. It is
/// a way to make a rare race appear, not a measure of how often anyone will meet it.
/// </para>
/// <para>
/// <b>The lesson worth more than the fix.</b> The companion race in <c>TimelineFlow</c> — an
/// action executed against a context watch the server had not registered yet — was visible as a
/// three-in-four failure when that class ran <em>alone</em>, and was filed for hours as
/// "isolation is not neutral for this class". Isolation was the instrument, not the interference.
/// A test that behaves differently by itself is reporting something.
/// </para>
/// </remarks>
public class AdpFileWriterSharingContractTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-sharing-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterSharingContractTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AReaderSharingDelete_DoesNotStopASaveFromReplacingTheFile()
    {
        // Arrange: exactly what SharedDocumentReader opens, which is what the application uses.
        var path = IoPath.Combine(_folder, "document.tml");
        File.WriteAllText(path, "before");
        using var reader = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        // Act.
        AdpFileWriter.Save(path, "after");

        // Assert: the publish landed while the handle was still open.
        Assert.Equal("after", File.ReadAllText(path));
    }

    [Fact]
    public void AReaderSharingOnlyRead_DeniesTheReplaceAndTheSaveSaysSo()
    {
        // Arrange: what File.ReadAllText opens - no Delete.
        var path = IoPath.Combine(_folder, "document.tml");
        File.WriteAllText(path, "before");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act & assert: the failure the flow test hit, made deterministic. It surfaces as one of
        // the pair every caller of Save already catches, which is why the store could report
        // "could not be written" and keep the edit rather than losing it.
        var thrown = Assert.ThrowsAny<Exception>(() => AdpFileWriter.Save(path, "after"));
        Assert.True(
            thrown is UnauthorizedAccessException or IOException,
            $"Save threw {thrown.GetType().Name}, which callers do not catch: {thrown.Message}");

        // And the original is intact - a denied replace must not truncate what was there.
        Assert.Equal("before", File.ReadAllText(path));
    }

    [Fact]
    public void ADeniedSave_LeavesNoTemporaryBehind()
    {
        // The other half of a failed publish: the scratch file is cleaned up before the failure
        // is allowed out, or the project folder fills with ~adp- debris nobody owns.
        var path = IoPath.Combine(_folder, "document.tml");
        File.WriteAllText(path, "before");

        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => AdpFileWriter.Save(path, "after"));
        }

        Assert.Empty(Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*"));
    }

}
