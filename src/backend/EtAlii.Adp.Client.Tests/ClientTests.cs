using Xunit;

namespace EtAlii.Adp.Client.Tests;

/// <summary>
/// <b>The client's vitest suite, reported here file by file and test by test.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>What this adds, since gate 1 already runs `npm test`.</b> That gate gives one verdict for 1399
/// tests: a red tells you the client is broken, not what broke. These cases carry the same run's
/// outcome as <b>132 file cases and 1399 test cases inside the .NET suite</b>, so a failure names the
/// file and the test and carries vitest's own assertion output. Both should keep running: the gate
/// is the fast fail, and this is the readable one. It is the same single run either way - see
/// <see cref="ClientTestRun"/> for why one run rather than one per file.
/// </para>
/// <para>
/// <b>A theory that enumerates nothing passes</b>, which is how a discovery bug would look exactly
/// like a green suite. So both theories carry a floor and a named member, and the file list is read
/// from the filesystem rather than from the run - a file the run never opened is still a case, and
/// fails for being missing rather than disappearing from the report.
/// </para>
/// <para>
/// <b>Its limits, stated rather than closed.</b> These cases are a MIRROR of one run, not a pin on
/// any test's identity.
/// <list type="bullet">
/// <item><description><b>A renamed test is invisible</b>: the case list is rebuilt from each run, so
/// the old name simply stops appearing and the new one appears. The count is unchanged, so no floor
/// fires. That is deliberate - pinning names would mean a second copy of the suite, edited on every
/// legitimate rename - but it does mean these cases cannot tell a rename from a
/// delete-and-add.</description></item>
/// <item><description><b>A skipped test passes its case</b>, because the assertion is "did not fail":
/// `it.skip` is the client suite's own statement that it should not run, and re-judging that here
/// would be this project overruling the suite it reports.</description></item>
/// <item><description><b>Removal IS caught</b>, by the two floors: deleting tests drops the count
/// below <see cref="KnownTests"/>, and deleting a file drops it below <see cref="KnownTestFiles"/>
/// or leaves the file on disk unreported.</description></item>
/// </list>
/// </para>
/// </remarks>
public class ClientTests
{
    /// <summary>The floors: what the tree held when this was written (2026-09-22).</summary>
    /// <remarks>
    /// Structure, not content: a discovery that finds fewer has stopped reading the tree. They are
    /// minimums, so adding tests never edits this file; losing a third of them fails loudly.
    /// </remarks>
    private const int KnownTestFiles = 132;

    private const int KnownTests = 1399;

    public static TheoryData<string> TestFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in ClientTestRun.TestFiles())
        {
            data.Add(file);
        }

        return data;
    }

    /// <summary>
    /// Every individual client test, as its own case. <b>This enumerates the run</b>, so listing these
    /// cases costs the one vitest run; the file cases below do not, being read from the filesystem.
    /// </summary>
    public static TheoryData<string, string> Tests()
    {
        var data = new TheoryData<string, string>();
        foreach (var test in ClientTestRun.Result.Files.SelectMany(file => file.Tests))
        {
            data.Add(test.File, test.FullName);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TestFiles))]
    public void EveryClientTestFile_Passes(string file)
    {
        // Arrange.
        var run = ClientTestRun.Result;
        Assert.True(run.Failure is null, run.Failure);

        // Act.
        var reported = run.File(file);

        // Assert.
        Assert.True(reported is not null, $"{file}: vitest reported no result for this file, so nothing ran it.");
        var failed = reported.Tests.Where(test => test.Status == "failed").ToList();
        Assert.True(
            reported.Status != "failed" && failed.Count == 0,
            $"{file} failed ({failed.Count} of {reported.Tests.Count} tests):{Environment.NewLine}"
            + string.Join(Environment.NewLine, failed.Select(test => $"  {test.FullName}{Environment.NewLine}{Indent(string.Join(Environment.NewLine, test.FailureMessages))}"))
            + (reported.Message.Length > 0 ? Environment.NewLine + reported.Message : ""));
    }

    [Theory]
    [MemberData(nameof(Tests))]
    public void EveryClientTest_Passes(string file, string fullName)
    {
        // Arrange.
        var run = ClientTestRun.Result;
        Assert.True(run.Failure is null, run.Failure);

        // Act.
        var reported = run.File(file)?.Tests.FirstOrDefault(test => test.FullName == fullName);

        // Assert.
        Assert.True(reported is not null, $"{file}: vitest reported no result for \"{fullName}\".");
        Assert.True(
            reported.Status != "failed",
            $"{file} > {fullName}:{Environment.NewLine}{Indent(string.Join(Environment.NewLine, reported.FailureMessages))}");
    }

    [Fact]
    public void TheDiscoveryReadsTheWholeTree()
    {
        // Arrange: the canaries, because a theory over an empty set is a green suite that tested
        // nothing. A floor on what was walked, and a member that must be among them.
        var files = ClientTestRun.TestFiles();

        // Assert.
        Assert.True(files.Count >= KnownTestFiles, $"found only {files.Count} client test files; expected at least {KnownTestFiles}.");
        Assert.Contains("src/client/src/App.test.tsx", files);
        Assert.Contains(files, file => file.StartsWith("src/diagrams/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRunReportsEveryFileTheTreeHolds()
    {
        // Arrange: the two halves must agree - every file on disk is in the report, and the report
        // holds at least as many tests as the tree did when this was written.
        var run = ClientTestRun.Result;
        Assert.True(run.Failure is null, run.Failure);

        // Act.
        var missing = ClientTestRun.TestFiles().Where(file => run.File(file) is null).ToList();
        var tests = run.Files.Sum(file => file.Tests.Count);

        // Assert.
        Assert.True(missing.Count == 0, "client test files the run never reported: " + string.Join(", ", missing));
        Assert.True(tests >= KnownTests, $"the run reported only {tests} tests; expected at least {KnownTests}.");
    }

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split(Environment.NewLine).Select(line => "    " + line));
}
