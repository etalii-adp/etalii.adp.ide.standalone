using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A failed publish says what became of the file it was replacing: the same file, gone, or recreated
/// while the publish ran - three answers "present, 132 bytes" cannot tell apart.
/// </summary>
/// <remarks>
/// The only measured producers of <c>0x80070497</c> are a concurrent replace and a delete-and-recreate
/// of the destination; the field records all said "present", which fits both "untouched" and
/// "recreated". Each shape is forced through the replace seam, and each record must carry its own
/// answer and neither of the others'.
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class AdpFileWriterDestinationIdentityTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-destination-identity-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterDestinationIdentityTests()
    {
        Directory.CreateDirectory(_folder);
        // The holder query is not this test's subject; answering at once keeps each failure quick.
        FileHolders.Query = _ => FileHolders.None;
    }

    public void Dispose()
    {
        FileHolders.Query = null;
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    private static IOException Wild() => new("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };

    private string RecordFor(string path, Action<string, string> replace)
    {
        using var logs = LogCapture.Start();
        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after\n", replace));
        // One warning ABOUT THIS path: the capture is process-wide.
        return Assert.Single(logs.Warnings, w => w.Contains(path, StringComparison.Ordinal) && w.Contains("Could not publish", StringComparison.Ordinal));
    }

    [Fact]
    public void ADestinationLeftAlone_IsTheSameFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A destination's file identity is read only on Windows.");
        var path = IoPath.Combine(_folder, "same.owm");
        File.WriteAllText(path, "before\n");

        var record = RecordFor(path, (_, _) => throw Wild());

        Assert.Contains("identity: same file as when this publish began", record, StringComparison.Ordinal);
        Assert.DoesNotContain("GONE", record, StringComparison.Ordinal);
        Assert.DoesNotContain("RECREATED", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ADestinationDeletedDuringThePublish_IsGone()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A destination's file identity is read only on Windows.");
        var path = IoPath.Combine(_folder, "gone.owm");
        File.WriteAllText(path, "before\n");

        var record = RecordFor(path, (_, destination) =>
        {
            File.Delete(destination);
            throw Wild();
        });

        Assert.Contains("identity: GONE since this publish began", record, StringComparison.Ordinal);
        Assert.DoesNotContain("RECREATED", record, StringComparison.Ordinal);
        Assert.DoesNotContain("same file", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ADestinationDeletedAndWrittenAgain_IsRecreated_ThoughItsCreationTimeSaysOtherwise()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A destination's file identity is read only on Windows.");
        // The shape the instrument exists for, and the reason it reads the file index: NTFS tunneling
        // hands a file recreated under the same name within ~15 s its predecessor's creation time, so
        // a creation-time comparison would have answered "same file" here. Both halves are asserted.
        var path = IoPath.Combine(_folder, "recreated.owm");
        File.WriteAllText(path, "before\n");
        var createdBefore = File.GetCreationTimeUtc(path);
        var createdAfter = DateTime.MinValue;

        var record = RecordFor(path, (_, destination) =>
        {
            File.Delete(destination);
            File.WriteAllText(destination, "someone else's\n");
            createdAfter = File.GetCreationTimeUtc(destination);
            throw Wild();
        });

        Assert.Contains("identity: RECREATED since this publish began", record, StringComparison.Ordinal);
        Assert.DoesNotContain("same file", record, StringComparison.Ordinal);
        Assert.DoesNotContain("GONE", record, StringComparison.Ordinal);
        Assert.True(
            createdBefore == createdAfter,
            $"Creation time moved from {createdBefore:O} to {createdAfter:O}: tunneling is off on this machine. "
            + "The instrument is unaffected - it reads the file index - but this half of the reason is not shown here.");
    }
}
