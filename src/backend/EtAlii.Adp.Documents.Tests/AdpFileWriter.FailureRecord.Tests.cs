using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A save that fails says what failed it, down to the Win32 code - because the one failure that
/// mattered went five captures without ever recording one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> <c>DiagramElementActionFlowTests</c> failed about once in fifty runs under load
/// with <c>IOException: "Unable to remove the file to be replaced."</c> out of
/// <c>File.Replace</c> in <see cref="AdpFileWriter.Save"/>. Five captures, one signature, two
/// entry points (adding a node and renaming one). Every server log recorded the type and the
/// message and none recorded the <c>HResult</c>, so the code a fix would need to match was never
/// seen.
/// </para>
/// <para>
/// <b>No reader reproduced it; two writers did.</b> Measured on 2026-09-15: a destination held with
/// <c>FileShare.Read</c> or <c>FileShare.None</c> fails with <c>0x80070020</c> (32, "being used
/// by another process"), a different message; a memory-mapped view does not stop the replace;
/// a reader racing 3000 saves produced 32 or nothing; a reader in <c>SharedDocumentReader</c>'s
/// own sharing mode broke none of 3000. Two writers racing on one path produced the wild
/// exception at once, 1009 times in 3000, and measured its code as <c>0x80070497</c> (1175) - a
/// suggestion of Architect 1's, after every reader had come back clean. For most of a day the only
/// instrument for the wild code was the failure itself, which is why these guards make sure the
/// failure records it: the next unexplained one should not take a day.
/// </para>
/// </remarks>
public class AdpFileWriterFailureRecordTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-failure-record-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterFailureRecordTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFailedReplace_LogsTheHResultThePathAndTheMessage()
    {
        // Arrange. The wild message with its measured code, 0x80070497, driven through the seam so
        // the record is asserted deterministically rather than by racing writers for it.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        using var logs = LogCapture.Start();

        // Act.
        var thrown = Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // Assert. The caller still gets the original exception - no error policy is imposed here.
        Assert.Same(wild, thrown);
        var warning = Assert.Single(logs.Warnings);
        Assert.Contains("0x80070497", warning, StringComparison.Ordinal);
        Assert.Contains(path, warning, StringComparison.Ordinal);
        Assert.Contains("Unable to remove the file to be replaced", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealHolderDenyingTheReplace_IsRecordedWithTheCodeTheOsActuallyGave()
    {
        // The half the seam cannot vouch for: a genuine OS refusal, through the real File.Replace,
        // records the code Windows actually returned. Measured as 0x80070020 for this arrangement.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        using var logs = LogCapture.Start();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => AdpFileWriter.Save(path, "after"));
        }

        Assert.Contains(logs.Warnings, warning => warning.Contains("0x80070020", StringComparison.Ordinal));
    }

    [Fact]
    public void ASuccessfulSave_LogsNoWarning()
    {
        // The must-not-catch half: a warning on every save would bury the one that matters.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        using var logs = LogCapture.Start();

        AdpFileWriter.Save(path, "after");

        Assert.Equal("after", File.ReadAllText(path));
        Assert.Empty(logs.Warnings);
    }
}
