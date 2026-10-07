using System.Diagnostics;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The inspection's evaluator (<c>.github/tools/inspect/evaluate.mjs</c>) judges a run's health
/// before its findings. A clean verdict must only ever come from a run that could see the code:
/// this tool once reported zero warnings here while carrying 327 unresolved-symbol errors.
/// </summary>
/// <remarks>
/// <para>
/// Each fact runs the evaluator on a report and a console log under
/// <c>.github/tools/inspect/fixtures/</c>, cut down from a real <c>jb inspectcode</c> run of this
/// repository, so the SARIF shape is the tool's and not an invention. The whole four-minute
/// inspection is not run here; <c>inspect.sh</c> is exercised end to end by the build workflow's
/// <c>inspection</c> job.
/// </para>
/// <para>
/// Every fact was seen to fail against the evaluator with the rule it tests removed (the
/// rider-warnings-cleanup implementation log for task 2 records each). <b>A missing node is a
/// failure, not a skip</b>: two of the four gates need node on every machine that builds this
/// repository, and a skip would pass exactly when the check did not run.
/// </para>
/// </remarks>
public class InspectScriptTests
{
    private const int Clean = 0;
    private const int Findings = 1;
    private const int Blind = 2;

    // seen.log names three inspected C# files, nothing-inspected.log none.
    private const string Tracked = "3";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task HealthyAndCleanExitsZero()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("clean.sarif", "seen.log");

        // Assert.
        Assert.True(exitCode == Clean && output.Contains("CLEAN:", StringComparison.Ordinal), Describe(exitCode, output));
    }

    [Fact]
    public async Task OneWarningExitsOneAndNamesIt()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("one-warning.sarif", "seen.log");

        // Assert.
        Assert.True(exitCode == Findings, Describe(exitCode, output));
        Assert.Contains("SkosLayout.Tests.cs:142  PossibleLossOfFraction", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneSuggestionExitsOneAndNamesIt()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("one-suggestion.sarif", "seen.log");

        // Assert.
        Assert.True(exitCode == Findings, Describe(exitCode, output));
        Assert.Contains("  UseIndexFromEndExpression  ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompilerErrorsAreBlind()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("compiler-errors.sarif", "seen.log");

        // Assert.
        Assert.True(exitCode == Blind && output.Contains("compiler error", StringComparison.Ordinal), Describe(exitCode, output));
    }

    [Fact]
    public async Task NothingInspectedIsBlind()
    {
        // Act. A valid, empty report: exactly what the run that found no toolset produced.
        (int exitCode, string output) = await Evaluate("clean.sarif", "nothing-inspected.log");

        // Assert.
        Assert.True(exitCode == Blind && output.Contains("inspected, below", StringComparison.Ordinal), Describe(exitCode, output));
    }

    [Fact]
    public async Task MissingReportIsBlind()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("no-such-report.sarif", "seen.log");

        // Assert.
        Assert.True(exitCode == Blind && output.Contains("missing", StringComparison.Ordinal), Describe(exitCode, output));
    }

    [Fact]
    public async Task ReportModeListsFindingsAndExitsZero()
    {
        // Act.
        (int exitCode, string output) = await Evaluate("one-warning.sarif", "seen.log", "--report");

        // Assert.
        Assert.True(exitCode == Clean, Describe(exitCode, output));
        Assert.Contains("  PossibleLossOfFraction  ", output, StringComparison.Ordinal);
    }

    private static string Describe(int exitCode, string output) =>
        $"evaluate.mjs exited {exitCode}:{Environment.NewLine}{output}";

    private static async Task<(int ExitCode, string Output)> Evaluate(string report, string log, params string[] options)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var tool = IoPath.Combine(LocateRepositoryRoot(), ".github", "tools", "inspect");
        var fixtures = IoPath.Combine(tool, "fixtures");
        string[] arguments =
        [
            IoPath.Combine(tool, "evaluate.mjs"),
            IoPath.Combine(fixtures, report),
            IoPath.Combine(fixtures, log),
            Tracked,
            .. options,
        ];

        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("node did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"evaluate.mjs did not finish within {Timeout}.");
        }

        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, ".github", "tools", "inspect")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "src", "backend")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (.github/tools/inspect beside src/backend) was not found above the test binary.");
    }
}
