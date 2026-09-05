using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

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

        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => AdpFileWriter.Save(path, "after"));
        }

        Assert.Empty(Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*"));
    }

}
