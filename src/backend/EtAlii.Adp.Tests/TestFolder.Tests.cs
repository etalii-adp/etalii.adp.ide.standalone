using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

/// <summary>
/// <c>TestFolder.TryDelete</c> gives up quietly no longer. The suite still stays green when a
/// folder survives - that decision is deliberate and unchanged - but the fact is recorded.
/// </summary>
/// <remarks>
/// Why this guard exists: 3,694 folders accumulated under
/// <c>%TEMP%/EtAlii.Adp.IntegrationTests/</c> and the only evidence anywhere was the folders
/// themselves. Two sessions inferred the mechanism from their contents and timestamps before
/// anyone instrumented the one method that already knew: `Directory.Exists` was still true on
/// the way out.
/// </remarks>
public class TestFolderTests
{
    [Fact]
    public void AFolderItCannotDelete_IsReportedRatherThanSwallowed()
    {
        // Arrange. A held file makes the recursive delete fail for real, every attempt, rather
        // than by a simulated clock - which is why this guard is deterministic and not a race.
        var folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.TestFolderGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var held = IoPath.Combine(folder, "held.txt");
        File.WriteAllText(held, "in use");
        var before = TestFolder.Failures.Count;

        using (var handle = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(handle.CanRead); // the handle is the arrangement, not decoration

            // Act.
            TestFolder.TryDelete(folder);

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
        Assert.True(File.Exists(TestFolder.ReportPath), "No report file, so a green run would leave no evidence at all.");
        Assert.Contains(folder, File.ReadAllText(TestFolder.ReportPath), StringComparison.Ordinal);

        // Cleanup: now that the handle is closed, the folder goes - and reports nothing.
        var afterCleanup = TestFolder.Failures.Count;
        TestFolder.TryDelete(folder);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(afterCleanup, TestFolder.Failures.Count);
    }

    [Fact]
    public void AFolderThatDeletesCleanly_ReportsNothing()
    {
        // The must-not-catch half: a report on every deletion would drown the one that matters.
        var folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.TestFolderGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IoPath.Combine(folder, "nested"));
        File.WriteAllText(IoPath.Combine(folder, "nested", "a.txt"), "content");
        var before = TestFolder.Failures.Count;

        TestFolder.TryDelete(folder);

        Assert.False(Directory.Exists(folder));
        Assert.Equal(before, TestFolder.Failures.Count);
    }

    [Fact]
    public void AFolderThatWasNeverThere_ReportsNothing()
    {
        var folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.TestFolderGuard", Guid.NewGuid().ToString("N"));
        var before = TestFolder.Failures.Count;

        TestFolder.TryDelete(folder);

        Assert.Equal(before, TestFolder.Failures.Count);
    }
}
