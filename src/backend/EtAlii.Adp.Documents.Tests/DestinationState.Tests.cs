using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A failed publish records what the destination looked like, which is the only half of the record
/// that can speak about an actor that has already let go.
/// </summary>
/// <remarks>
/// Architect 1's instrument, and the necessary companion to <see cref="FileHolders"/>: Restart
/// Manager names a process still HOLDING the file, while the dominant producer of
/// <c>0x80070497</c> - a completed delete, 185 failures in 400 measured replaces - holds nothing by
/// the time anyone asks. "Gone" against "sitting beside somebody else's scratch file" is the
/// distinction that survives the actor leaving.
/// </remarks>
public class DestinationStateTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-destination-state-" + Guid.NewGuid().ToString("N"));

    public DestinationStateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFailedPublishWhoseDestinationWasDeleted_RecordsThatItIsGone()
    {
        // The case no holder query can answer: the deleter finished and holds nothing.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        using var logs = LogCapture.Start();

        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) =>
        {
            File.Delete(path);
            throw wild;
        }));

        var warning = Assert.Single(logs.Warnings);
        Assert.Contains("GONE", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedPublishBesideAnotherPublishersScratchFile_NamesIt()
    {
        // The other half of the same distinction: the destination is still there, and somebody
        // else's publish is visibly in flight around it.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        var intruder = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}somebodyelse{AdpFileWriter.TempExtension}");
        File.WriteAllText(intruder, "another publisher's scratch");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        using var logs = LogCapture.Start();

        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        var warning = Assert.Single(logs.Warnings);
        Assert.Contains("publishes in flight beside it", warning, StringComparison.Ordinal);
        Assert.Contains(IoPath.GetFileName(intruder), warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ADestinationNobodyIsPublishing_SaysSoRatherThanSayingNothing()
    {
        // An absent phrase reads as an unasked question, so the quiet case is stated too.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");

        var described = DestinationState.Describe(path);

        Assert.Contains("present", described, StringComparison.Ordinal);
        Assert.Contains("no publish in flight beside it", described, StringComparison.Ordinal);
    }

    [Fact]
    public void ADestinationWhoseFolderIsGone_IsDescribedRatherThanThrowing()
    {
        // It runs inside a failure path: a diagnostic that throws would replace the failure it was
        // meant to explain.
        var described = DestinationState.Describe(IoPath.Combine(_folder, "vanished", "tea.owm"));

        Assert.Contains("GONE", described, StringComparison.Ordinal);
        Assert.Contains("folder is gone too", described, StringComparison.Ordinal);
    }
}
