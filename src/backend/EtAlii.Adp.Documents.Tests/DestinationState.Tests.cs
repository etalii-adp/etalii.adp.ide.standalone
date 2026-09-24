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

        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(
            logs.Warnings,
            w => w.Contains(path, StringComparison.Ordinal) &&
                 w.Contains("GONE", StringComparison.Ordinal));
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

        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(
            logs.Warnings,
            w => w.Contains(path, StringComparison.Ordinal) &&
                 w.Contains("SOMEBODY ELSE was publishing beside it", StringComparison.Ordinal));
        Assert.Contains("SOMEBODY ELSE was publishing beside it", warning, StringComparison.Ordinal);
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
        Assert.Contains("nobody else was publishing beside it", described, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedPublish_TellsTheDescriptionWhichScratchFileIsItsOwn()
    {
        // THE WIRING, not the unit. The two guards below call Describe directly and pass the
        // scratch name themselves, so they would stay green while the PUBLISH forgot to pass it -
        // and the field record would quietly go back to calling our own temporary somebody else.
        // Measured: with the argument dropped at the call site, those two guards notice nothing.
        // This one drives the real Save, whose temporary exists only during the failure it is
        // describing.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        using var logs = LogCapture.Start();

        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(
            logs.Warnings,
            w => w.Contains(path, StringComparison.Ordinal) &&
                 w.Contains("nobody else was publishing beside it, only our own", StringComparison.Ordinal));
        Assert.Contains("nobody else was publishing beside it, only our own", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("SOMEBODY ELSE", warning, StringComparison.Ordinal);
    }
    [Fact]
    public void OurOwnScratchFile_IsNotReportedAsSomebodyElsePublishing()
    {
        // THE MISREADING THIS FIXES. Describe runs inside the failing publish, before that
        // publish removes its temporary, so exactly one ~adp-* file is the ordinary state of
        // every failure. Listing it unqualified said "another publisher was here" about the
        // reader's own process - and Architect 1 read the fourth field occurrence exactly that
        // way before catching it.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var ourScratch = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}ourown{AdpFileWriter.TempExtension}");
        File.WriteAllText(ourScratch, "this publish's own scratch");

        var described = DestinationState.Describe(path, ourScratch);

        Assert.DoesNotContain("SOMEBODY ELSE", described, StringComparison.Ordinal);
        Assert.Contains("nobody else was publishing beside it", described, StringComparison.Ordinal);
        // Kept rather than hidden: that our own publish was mid-flight is a fact worth having.
        Assert.Contains(IoPath.GetFileName(ourScratch), described, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherPublishersScratchFile_IsNamedAsTheirs_BesidesOurs()
    {
        // The case the line exists for, and it must stay unmistakable now that ours is named
        // too: a reader has to be able to tell one file from two at a glance.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var ourScratch = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}ourown{AdpFileWriter.TempExtension}");
        var theirScratch = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}somebodyelse{AdpFileWriter.TempExtension}");
        File.WriteAllText(ourScratch, "ours");
        File.WriteAllText(theirScratch, "theirs");

        var described = DestinationState.Describe(path, ourScratch);

        Assert.Contains("SOMEBODY ELSE was publishing beside it", described, StringComparison.Ordinal);
        Assert.Contains(IoPath.GetFileName(theirScratch), described, StringComparison.Ordinal);
        Assert.Contains($"besides our own {IoPath.GetFileName(ourScratch)}", described, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoScratchFileOfOurs_AStrangersIsStillTheirs()
    {
        // Describe is public and can be called without a publish in hand - the attribution must
        // not depend on being told about ours.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        var theirScratch = IoPath.Combine(_folder, $"{AdpFileWriter.TempPrefix}somebodyelse{AdpFileWriter.TempExtension}");
        File.WriteAllText(theirScratch, "theirs");

        var described = DestinationState.Describe(path);

        Assert.Contains("SOMEBODY ELSE was publishing beside it", described, StringComparison.Ordinal);
        Assert.DoesNotContain("our own", described, StringComparison.Ordinal);
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
