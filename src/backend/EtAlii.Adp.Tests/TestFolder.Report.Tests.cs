using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests;

/// <summary>
/// Where <c>TestFolder</c>'s teardown report goes, and that it keeps every line - so a gate can keep
/// a run's report without keeping other runs' entries or losing its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The report was one file under %TEMP% that every test process on the machine appended
/// to. Architect 1 asked, before building the gate side, what a kept copy would contain: other
/// sessions' runs mixed with its own. And appends from parallel tests contend for the file handle,
/// the loser's <c>IOException</c> is swallowed by design so a report cannot fail a test, and so a
/// line can disappear without a trace. The same contention failed six tests on 2026-09-12, when a
/// diagnostic probe appended to one log from parallel tests; here it would fail nothing and lose
/// the evidence instead.
/// </para>
/// <para>
/// <b>The contract, agreed with Architect 1.</b> <c>ADP_UNDELETED_FOLDERS_DIR</c> names a
/// directory, and each test process writes its own <c>&lt;pid&gt;.log</c> inside it. Unset, a local
/// run keeps the %TEMP% file. A lock serialises the threads inside one process, because one test
/// assembly is one process running many classes in parallel - one file per process separates
/// processes and does nothing for the threads within one.
/// </para>
/// </remarks>
public class TestFolderReportTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.TestFolderReport", Guid.NewGuid().ToString("N"));

    public TestFolderReportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ConcurrentReportsWithinOneProcess_LoseNoLine()
    {
        // Arrange. Many threads released together, each appending its own distinct lines to one
        // report file - one test assembly's parallel classes reporting at once.
        var target = IoPath.Combine(_folder, "report.log");
        const int threads = 32;
        const int linesEach = 100;
        using var barrier = new Barrier(threads);

        // Act.
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            // ReSharper disable once AccessToDisposedClosure
            // Reason: Used in a test case which is acceptable.
            barrier.SignalAndWait();
            for (var i = 0; i < linesEach; i++)
            {
                TestFolder.Append(target, $"thread {t} line {i}");
            }
        })).ToArray();
        foreach (var worker in workers)
        {
            worker.Start();
        }
        foreach (var worker in workers)
        {
            worker.Join();
        }

        // Assert. Every line, exactly once, intact.
        var written = File.ReadAllLines(target);
        var expected = Enumerable.Range(0, threads).SelectMany(t => Enumerable.Range(0, linesEach).Select(i => $"thread {t} line {i}")).ToHashSet();
        Assert.True(written.Length == threads * linesEach, $"{threads * linesEach - written.Length} of {threads * linesEach} report lines were lost.");
        Assert.True(expected.SetEquals(written), "The report holds a torn, duplicated or foreign line.");
    }

    [Fact]
    public void WithADirectoryNamed_EachProcessWritesItsOwnFile()
    {
        // Separation between processes is by construction: each file is named for its process id,
        // and every test assembly is its own process. What is asserted is exactly that naming.
        var first = TestFolder.ReportFileFor(_folder, 1234);
        var second = TestFolder.ReportFileFor(_folder, 5678);

        Assert.Equal(IoPath.Combine(_folder, "1234.log"), first);
        Assert.Equal(IoPath.Combine(_folder, "5678.log"), second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void WithNoDirectoryNamed_ALocalRunKeepsTheTempFile()
    {
        var expected = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.undeleted-test-folders.log");

        Assert.Equal(expected, TestFolder.ReportFileFor(null, 1234));
        Assert.Equal(expected, TestFolder.ReportFileFor("", 1234));
        Assert.Equal(expected, TestFolder.ReportFileFor("   ", 1234));
    }

    [Fact]
    public void AReportToADirectoryThatDoesNotExistYet_CreatesIt()
    {
        // The gate creates the directory, but a report must not depend on that: a missing directory
        // would otherwise swallow the line in the catch that exists for contention.
        var target = IoPath.Combine(_folder, "not-yet", "4321.log");

        TestFolder.Append(target, "a line");

        Assert.Equal(["a line"], File.ReadAllLines(target));
    }
}
