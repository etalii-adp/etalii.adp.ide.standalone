using System.Diagnostics;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A failed publish names WHO held the file, not only what went wrong - and asking must never
/// change what the save does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> <c>0x80070497</c> recurred on a gate on 2026-09-20 with no
/// "Waited for another save" line, which says only that the other party was outside this
/// process's lock. Measured against a live holder, 400 replaces each: a concurrent
/// delete-and-recreate of the destination produced <c>0x80070497</c> 185 times, a second replacer
/// 29 times in 800, and an editor-shaped writer (<c>FileMode.Create</c>, <c>FileShare.Read</c>)
/// not once - it produced <c>0x20</c>, <c>0x498</c> and <c>0x499</c> instead. The code narrows the
/// shape of the other party and never its identity, so the record now asks the operating system
/// who is holding the file.
/// </para>
/// <para>
/// <b>What these guards do not claim.</b> Restart Manager lists holders at the moment it is asked,
/// so an actor that has already released is invisible to it - a completed delete above all, which
/// is the dominant producer of the very code that prompted this. That case is the
/// <see cref="FileHolders.None"/> answer, and it is asserted as its own outcome rather than left
/// to look like a failed query.
/// </para>
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class AdpFileWriterHolderRecordTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-holder-record-" + Guid.NewGuid().ToString("N"));

    public AdpFileWriterHolderRecordTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        FileHolders.Query = null;
        FileHolders.Budget = TimeSpan.FromSeconds(2);
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFailedPublish_NamesAnotherProcessHoldingTheFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The holders of a file are asked only on Windows.");
        // The case the gate hit: the other party is not in this process, so nothing in this process
        // can name it. A real second process holds the file; the record must carry its pid.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        var ready = IoPath.Combine(_folder, "holder.ready");
        // The real Restart Manager talks to a service, and this guard is about what the RECORD
        // SAYS rather than about how busy the machine is: at the production budget a loaded gate
        // timed out the query and reddened this test for a reason it does not pin. The timeout
        // itself is pinned by AHolderQueryThatHangs_DoesNotHoldTheSaveOpen.
        FileHolders.Budget = TimeSpan.FromSeconds(30);


        using var holder = StartHolder(path, ready);
        try
        {
            WaitForHolder(ready, holder);
            using var logs = LogCapture.Start();

            Assert.ThrowsAny<IOException>(() => AdpFileWriter.Save(path, "after"));

            // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
            // concurrent test's warning must not redden this one. An absent line still fails.
            var warning = Assert.Single(
                logs.Warnings,
                w => w.Contains(path, StringComparison.Ordinal) &&
                     w.Contains("Could not publish", StringComparison.Ordinal));
            Assert.Contains($"pid {holder.Id}", warning, StringComparison.Ordinal);
            Assert.Contains($"this process is pid {Environment.ProcessId}", warning, StringComparison.Ordinal);
        }
        finally
        {
            StopHolder(holder);
        }
    }

    [Fact]
    public void AFailedPublish_SaysSoWhenThisProcessIsTheHolder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A handle's sharing mode denies a replace or delete only on Windows.");
        // The other half of naming: a handle held here is ours, and a record that did not say so
        // would send the next reader hunting for a process that was never involved.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        // The real Restart Manager talks to a service, and this guard is about what the RECORD
        // SAYS rather than about how busy the machine is: at the production budget a loaded gate
        // timed out the query and reddened this test for a reason it does not pin. The timeout
        // itself is pinned by AHolderQueryThatHangs_DoesNotHoldTheSaveOpen.
        FileHolders.Budget = TimeSpan.FromSeconds(30);
        using var logs = LogCapture.Start();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => AdpFileWriter.Save(path, "after"));
        }

        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(
            logs.Warnings,
            w => w.Contains(path, StringComparison.Ordinal) &&
                 w.Contains("Could not publish", StringComparison.Ordinal));
        Assert.Contains($"pid {Environment.ProcessId}", warning, StringComparison.Ordinal);
        Assert.Contains("THIS process", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AHolderQueryThatThrows_LeavesTheSaveFailingExactlyAsItDid()
    {
        // THE CONDITION ON THE WHOLE CHANGE: a diagnostic may not change the outcome it describes.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        FileHolders.Query = _ => throw new InvalidOperationException("the Restart Manager is not available here");
        using var logs = LogCapture.Start();

        var thrown = Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // Same exception, same record, and the failed query says it failed rather than saying nobody.
        Assert.Same(wild, thrown);
        // One warning ABOUT THIS, not one warning in total: the capture is process-wide, so a
        // concurrent test's warning must not redden this one. An absent line still fails.
        var warning = Assert.Single(
            logs.Warnings,
            w => w.Contains(path, StringComparison.Ordinal) &&
                 w.Contains("holders could not be determined", StringComparison.Ordinal));
        Assert.Contains("0x80070497", warning, StringComparison.Ordinal);
        Assert.Contains("holders could not be determined", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(FileHolders.None, warning, StringComparison.Ordinal);
        Assert.Equal("before", File.ReadAllText(path));
    }

    [Fact]
    public void AHolderQueryThatHangs_DoesNotHoldTheSaveOpen()
    {
        // "Slow" is the failure mode a timeout hides badly: without a budget the save waits on a
        // service that may never answer. Thirty seconds of hang must cost the save a few.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        FileHolders.Budget = TimeSpan.FromMilliseconds(200);
        FileHolders.Query = _ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(30));
            return "never gets here";
        };
        using var logs = LogCapture.Start();

        var clock = Stopwatch.StartNew();
        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The save waited {clock.Elapsed.TotalSeconds:0.0}s on a holder query with a 200ms budget.");
        Assert.Contains(logs.Warnings, warning => warning.Contains("not yet known", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Warnings, warning => warning.Contains(FileHolders.None, StringComparison.Ordinal));
    }

    [Fact]
    public void ASuccessfulSave_AsksNobodyWhoIsHoldingAnything()
    {
        // The must-not-catch half: the query costs a service call, and a save that succeeds has no
        // question to ask.
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");

        // ASKED ABOUT THIS PATH, not asked at all. FileHolders.Query is a static seam and the
        // assembly runs its classes in parallel, so a DIFFERENT class's deliberately failing
        // save calls whatever query is installed at that moment - and a plain call count made
        // this guard fail for somebody else's failure. Counting by path asks the question this
        // test is actually about and cannot be answered by another test's work.
        var asked = new System.Collections.Concurrent.ConcurrentBag<string>();
        FileHolders.Query = queried =>
        {
            asked.Add(queried);
            return "asked";
        };

        AdpFileWriter.Save(path, "after");

        Assert.DoesNotContain(path, asked);
        Assert.Equal("after", File.ReadAllText(path));
    }

    [Fact]
    public void AFileNobodyHolds_IsReportedAsNobodyHoldingIt_NotAsAFailedQuery()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The holders of a file are asked only on Windows.");
        // The limit, asserted rather than described: the dominant producer of 0x80070497 is an
        // actor that has already released, and the record must say "nobody now" in words a reader
        // cannot mistake for "the query broke".
        var path = IoPath.Combine(_folder, "tea.owm");
        File.WriteAllText(path, "before");

        // THE BUDGET ITS TWO SIBLINGS ALREADY HAVE, for the reason they give: this guard is about what
        // the RECORD SAYS, and the timeout is pinned by AHolderQueryThatHangs_DoesNotHoldTheSaveOpen.
        // At the production two seconds a full parallel gate timed the real query out and this read
        // "holders not yet known after 2s" (FDG task 12's gate, 2026-09-25, found by Developer 1) - the
        // third test in this class to meet that, and the one the first two fixes missed.
        //
        // NOT an injected answer through FileHolders.Query, which would be deterministic and would test
        // the wrong thing: the subject includes the REAL Restart Manager saying nobody, and the mapping
        // of its zero-holder answer to None rather than to "could not be determined". Injected, that
        // mapping would go unexercised and this would test only how a supplied string is formatted.
        FileHolders.Budget = TimeSpan.FromSeconds(30);

        var described = FileHolders.Describe(path);

        Assert.Contains(FileHolders.None, described, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be determined", described, StringComparison.Ordinal);
        Assert.Contains("answered in", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second process holding <paramref name="path"/> open, which is what makes this a test of
    /// naming another process rather than of naming ourselves.
    /// </summary>
    private static Process StartHolder(string path, string ready)
    {
        var script =
            $"$h = [System.IO.File]::Open('{path}', 'Open', 'Read', 'Read'); " +
            $"Set-Content -Path '{ready}' -Value 'holding'; " +
            "Start-Sleep -Seconds 60; $h.Dispose()";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);

        return Process.Start(start) ?? throw new InvalidOperationException("The holder process did not start.");
    }

    private static void WaitForHolder(string ready, Process holder)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!File.Exists(ready) && DateTime.UtcNow < deadline)
        {
            Assert.False(holder.HasExited, "The arrangement failed: the holder process exited before it held the file.");
            Thread.Sleep(50);
        }

        // Loud rather than silent: without a real holder this test would pass for the wrong reason.
        Assert.True(File.Exists(ready), "The arrangement failed: the holder process never reported holding the file.");
    }

    private static void StopHolder(Process holder)
    {
        try
        {
            if (!holder.HasExited)
            {
                holder.Kill(entireProcessTree: true);
                holder.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone; nothing to stop.
        }
    }
}
