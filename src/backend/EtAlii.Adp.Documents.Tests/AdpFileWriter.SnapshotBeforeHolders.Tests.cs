using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A failed publish describes its folder as it was WHEN THE PUBLISH FAILED, not as it was once the
/// holder query had finished.
/// </summary>
/// <remarks>
/// <b>Why.</b> The destination used to be the record's last argument, so it was read after
/// <see cref="FileHolders.Describe"/>, which waits up to its budget - 728, 1,132 and 1,166 ms in
/// the three field records that time it. A competing ADP publish creates its scratch file,
/// replaces and deletes it well inside that, so by the time the folder was read it had left no
/// trace and the record said "only our own" in exactly the case the sibling scan exists to catch.
/// Here the holder query itself plays that competitor finishing: it deletes the competitor's
/// scratch file while it is being waited on. Read before the query, the folder still shows it.
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class AdpFileWriterSnapshotBeforeHoldersTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-snapshot-order-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterSnapshotBeforeHoldersTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        FileHolders.Query = null;
        FileHolders.Budget = TimeSpan.FromSeconds(2);
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ACompetingPublishThatFinishesDuringTheHolderQuery_IsStillNamed()
    {
        var path = IoPath.Combine(_folder, "plan.tml");
        File.WriteAllText(path, "before");
        var competitor = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}competitor{AdpFileWriter.TempExtension}");
        File.WriteAllText(competitor, "another publisher's scratch");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };

        // Inline, not late: the reordering is about what happens while the record is being built,
        // so the query must answer inside the budget rather than on the late-answer path.
        FileHolders.Budget = TimeSpan.FromSeconds(30);
        // ONLY FOR THIS PATH. The seam is static and classes outside this collection still run
        // failing saves in parallel; the competitor must vanish because of THIS publish's query.
        var competitorFinished = false;
        FileHolders.Query = asked =>
        {
            if (string.Equals(asked, path, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(competitor);
                competitorFinished = true;
            }
            return "pid 4242 nobody-in-particular.exe";
        };
        using var logs = LogCapture.Start();

        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // The query really ran and really removed it - otherwise this would pass for either order.
        Assert.True(competitorFinished, "the holder query was never asked about this path");
        Assert.False(File.Exists(competitor));
        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(logs.Warnings, w => w.Contains(path, StringComparison.Ordinal));
        Assert.Contains("SOMEBODY ELSE was publishing beside it", warning, StringComparison.Ordinal);
        Assert.Contains(IoPath.GetFileName(competitor), warning, StringComparison.Ordinal);
    }
}
