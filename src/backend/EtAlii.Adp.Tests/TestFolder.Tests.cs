using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

/// <summary>
/// <c>TestFolder.TryDelete</c> gives up quietly no longer. The suite still stays green when a
/// folder survives - that decision is deliberate and unchanged - but the fact is recorded.
/// </summary>
/// <remarks>
/// <para>
/// Why this guard exists: 3,694 folders accumulated under
/// <c>%TEMP%/EtAlii.Adp.IntegrationTests/</c> and the only evidence anywhere was the folders
/// themselves. Two sessions inferred the mechanism from their contents and timestamps before
/// anyone instrumented the one method that already knew: `Directory.Exists` was still true on
/// the way out.
/// </para>
/// <para>
/// <b>These guards cause failures on purpose, and so they report to their own file and clean up
/// their own folders.</b> They first reported to the real report and left the seam guard's folder
/// behind: Architect 1, counting report lines against leftover folders across three real gates,
/// found exactly two report lines per green run and three surviving folders - all from these
/// tests, and 21 of them on this machine by the time it was noticed. A report that grows on every
/// passing run is the noise that hides a real entry, which is the opposite of what it is for.
/// </para>
/// </remarks>
public class TestFolderTests
{
    [Fact]
    public void AFolderItCannotDelete_IsReportedRatherThanSwallowed()
    {
        // Arrange. A held file makes the recursive delete fail for real, every attempt, rather
        // than by a simulated clock - which is why this guard is deterministic and not a race.
        var (root, folder, report) = Scratch();
        Directory.CreateDirectory(folder);
        var held = IoPath.Combine(folder, "held.txt");
        File.WriteAllText(held, "in use");
        var before = TestFolder.Failures.Count;

        try
        {
            using (var handle = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.True(handle.CanRead); // the handle is the arrangement, not decoration

                // Act.
                TestFolder.TryDelete(folder, Directory.Exists, caller: null, reportTarget: report);

                // Assert, first: it really could not delete it, so the report is about a true failure.
                Assert.True(Directory.Exists(folder), "The arrangement failed: the folder was deleted despite the held handle.");
            }

            // Assert. THE REPORT IS THE BEHAVIOUR.
            var reported = TestFolder.Failures.Skip(before).ToArray();
            var line = Assert.Single(reported);
            Assert.Contains(folder, line, StringComparison.Ordinal);
            Assert.Contains("gave up deleting", line, StringComparison.Ordinal);

            // And it survives a green run: the test platform prints console output only for tests
            // that fail, so the file is the half that is still there when nothing went red.
            Assert.True(File.Exists(report), "No report file, so a green run would leave no evidence at all.");
            Assert.Contains(folder, File.ReadAllText(report), StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root, report);
        }
    }

    [Fact]
    public void AFolderThatSurvivesADeleteThatDidNotThrow_IsStillReported()
    {
        // THE CASE THE FIRST VERSION OF THIS REPORTING MISSED, and the one that actually produces
        // the litter. Measured across one full run: 870 teardowns, 856 folders gone, and 14 where
        // Directory.Delete(recursive: true) returned with NO exception and the directory was still
        // there - exactly that run's leftover count. Trusting the non-throw, the helper returned
        // happily and reported nothing through all 14.
        //
        // Provoked deterministically rather than waited for: a writer that keeps recreating the
        // folder means every attempt's delete succeeds and every check still finds it there. In
        // the wild the writer is a debounced cache write landing mid-delete, which happens 14
        // times in 870 - far too rare for a guard to wait on.
        // I first tried to provoke it for real, with a thread recreating the folder as the delete
        // ran. It passed 3 runs in 5, caught each time by this test's own arrangement assertion
        // rather than by a green run - and a flaky guard teaches people that red means noise. So
        // the existence check is handed in instead: the same loop, the same report, put into the
        // state that happens 14 times in 870 without waiting for luck.
        var (root, folder, report) = Scratch();
        Directory.CreateDirectory(folder);
        var before = TestFolder.Failures.Count;
        var deletes = 0;

        try
        {
            // Act. The folder is put back before each check, so every attempt's delete succeeds
            // without throwing and every check still finds it there - the wild case exactly, where a
            // write lands mid-delete. (Returning true without recreating would make the second
            // attempt's Delete throw DirectoryNotFoundException, which is an IOException, and the
            // report would name that instead of the silent survival this guard is about.)
            TestFolder.TryDelete(
                folder,
                _ =>
                {
                    deletes++;
                    Directory.CreateDirectory(folder);
                    return true;
                },
                caller: @"x\FakeCaller.Tests.cs",
                reportTarget: report);

            // Assert.
            Assert.True(deletes >= 5, $"The loop gave up early: only {deletes} existence checks.");
            var reported = TestFolder.Failures.Skip(before).ToArray();
            var line = Assert.Single(reported);
            Assert.Contains(folder, line, StringComparison.Ordinal);
            Assert.Contains("still present, no exception", line, StringComparison.Ordinal);
            // And it names who called, which is what turns the report file into an answer to
            // "which classes fail?" rather than a list of GUIDs.
            Assert.Contains("FakeCaller.Tests.cs", line, StringComparison.Ordinal);
        }
        finally
        {
            // The folder the fake existence check kept recreating is still there; this is the
            // guard that used to leave it behind on every run.
            CleanUp(root, report);
        }
    }

    [Fact]
    public void AFolderThatDeletesCleanly_ReportsNothing()
    {
        // The must-not-catch half: a report on every deletion would drown the one that matters.
        var (root, folder, report) = Scratch();
        Directory.CreateDirectory(IoPath.Combine(folder, "nested"));
        File.WriteAllText(IoPath.Combine(folder, "nested", "a.txt"), "content");
        var before = TestFolder.Failures.Count;

        try
        {
            TestFolder.TryDelete(folder, Directory.Exists, caller: null, reportTarget: report);

            Assert.False(Directory.Exists(folder));
            Assert.Equal(before, TestFolder.Failures.Count);
            Assert.False(File.Exists(report), "A clean delete wrote a report line.");
        }
        finally
        {
            CleanUp(root, report);
        }
    }

    [Fact]
    public void AFolderThatWasNeverThere_ReportsNothing()
    {
        var (root, folder, report) = Scratch();
        var before = TestFolder.Failures.Count;

        try
        {
            TestFolder.TryDelete(folder, Directory.Exists, caller: null, reportTarget: report);

            Assert.Equal(before, TestFolder.Failures.Count);
            Assert.False(File.Exists(report), "A folder that was never there wrote a report line.");
        }
        finally
        {
            CleanUp(root, report);
        }
    }

    /// <summary>A scratch root holding the folder under test and this test's own report file.</summary>
    private static (string Root, string Folder, string Report) Scratch()
    {
        var root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.TestFolderGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, IoPath.Combine(root, "subject"), IoPath.Combine(root, "report.log"));
    }

    /// <summary>
    /// Removes the whole scratch root, and fails if it survives: a guard about leftover folders
    /// must not be one. Reports to the test's own file, never the real report.
    /// </summary>
    private static void CleanUp(string root, string report)
    {
        TestFolder.TryDelete(root, Directory.Exists, caller: null, reportTarget: report);
        Assert.False(Directory.Exists(root), $"This guard left its scratch folder behind: {root}");
    }
}
