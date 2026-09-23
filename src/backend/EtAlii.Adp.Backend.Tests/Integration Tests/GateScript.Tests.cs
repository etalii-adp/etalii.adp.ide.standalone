using System.Diagnostics;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The shared gate (<c>.github/tools/gate/</c>) passes its own self-test. The gate is how every
/// change reaches <c>develop</c>, so a regression in it has to fail a gate here - on the merged
/// tree, before it lands - rather than show up afterwards as a bad landing.
/// </summary>
/// <remarks>
/// <para>
/// The self-test is a bash script because the gate is one: it builds a throwaway repository
/// holding a husk, a feature worktree and a main checkout, and runs the real scripts against
/// them. This class only runs it and reads its verdict. It is hermetic - nothing outside a temp
/// folder changes - and it runs no npm or dotnet.
/// </para>
/// <para>
/// <b>A missing bash is a failure, not a skip.</b> A skip would pass exactly when the check did
/// not run, which is the defect the gate itself exists to refuse. Every machine that builds this
/// repository has git, and git for Windows carries bash. On Windows it is located through git's
/// own installation and never from <c>PATH</c>, where <c>System32\bash.exe</c> would start WSL.
/// </para>
/// <para>
/// <b>A product branch runs the quick subset, not the whole suite</b>, and the full suite runs only
/// when the branch changes <c>.github/tools/gate/</c>. The full run costs 1274 s measured on a quiet
/// machine; the subset costs 16.8 s and covers 119 of 177 cases. What it drops is the two sections
/// that build throwaway repositories end to end; what it keeps is the guard's refusals, the verdict
/// parsing, the logs, the tell and the argument handling.
/// </para>
/// <para>
/// <b>What that costs is less than it sounds like, and saying so precisely is the point.</b> Every
/// gate run exercises <c>gate.sh</c>'s happy path by definition - it is the thing running - so a
/// break there fails loudly on the next gate whether or not this test ran. What the self-test covers
/// and ordinary operation does not is the <b>refusal</b> paths: a husk refused, the main checkout
/// refused, a stale verdict refused. Those are in the quick subset. <b>What is genuinely unguarded
/// on a product branch is the end-to-end behaviour of <c>gate.sh</c>, <c>land.sh</c> and
/// <c>retire.sh</c> against a real repository</b>, and only a change outside the gate directory could
/// break that without re-running it - by moving something those scripts assume. That is a real gap
/// and it is stated rather than papered over.
/// </para>
/// <para>
/// <b>Why the minutes mattered enough to do this.</b> The full suite drives a few hundred git
/// processes for twenty-one minutes <em>in parallel with every other test</em>. On 2026-09-23 a gate
/// failed on two unrelated tests - a 60-second gRPC deadline, and an assertion that a failure record
/// holds one log line where contention adds a second - both of which that load explains and neither
/// of which reproduces alone. The link is a coincidence in time rather than a measured cause, and
/// removing the load from product gates settles it either way: if the storm was the cause the
/// failures stop, and if it was not they recur and cost nothing to observe.
/// </para>
/// <para>
/// Green needs both halves of the script's answer: exit code 0 <b>and</b> its
/// <c>RESULT=selftest-green</c> line, which it prints only when every expected case ran and none
/// was wrong.
/// </para>
/// </remarks>
public class GateScriptTests
{
    // Generous on purpose: the suite drives a few hundred git processes, which took 35 s on a quiet
    // machine and 209 s on a slow one the same day. A timeout here fails a gate for the wrong reason;
    // nothing in the suite waits for input, so a real hang is the unlikely case.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task SelfTestIsGreen()
    {
        // Arrange.
        var cancellation = TestContext.Current.CancellationToken;
        var root = LocateRepositoryRoot();
        var script = IoPath.Combine(root, ".github", "tools", "gate", "gate.test.sh");
        Assert.True(File.Exists(script), $"The shared gate's self-test is missing: {script}");
        var bash = await LocateBash(cancellation);

        var full = await GateDirectoryChanged(root, cancellation);
        string[] arguments = full
            ? [script.Replace('\\', '/')]
            : [script.Replace('\\', '/'), "--quick"];

        // Act.
        var (exitCode, output) = await Run(bash, arguments, root, cancellation);

        // Assert. The two verdicts contain neither the other, so this cannot be satisfied by the
        // wrong one - including by an older script that ignores --quick and runs everything.
        var expected = full ? "RESULT=selftest-green" : "RESULT=selftest-quick-green";
        Assert.True(
            exitCode == 0 && output.Contains(expected, StringComparison.Ordinal),
            $"The shared gate's self-test is not green in {(full ? "full" : "quick")} mode " +
            $"(exit {exitCode}, wanted {expected}):{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Whether this branch changes the gate itself, and therefore has to re-prove all of it.
    /// </summary>
    /// <remarks>
    /// <b>Every answer it cannot establish is <c>true</c>.</b> A missing <c>develop</c>, a git that
    /// fails, a shallow clone: each runs the full suite. The expensive direction is the safe one, and
    /// a filter that guessed <em>quick</em> when it could not tell would narrow coverage exactly where
    /// the repository is least ordinary.
    /// </remarks>
    private static async Task<bool> GateDirectoryChanged(string root, CancellationToken cancellation)
    {
        // develop...HEAD is the branch's own changes since it diverged, which is what a gate merges.
        // In the gate's scratch tree HEAD is the merge commit and develop is the base, so the same
        // expression answers the same question there.
        var (exitCode, output) = await Run(
            "git",
            ["diff", "--name-only", "develop...HEAD", "--", ".github/tools/gate/"],
            root,
            cancellation);
        return exitCode != 0 || output.Trim().Length > 0;
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, ".github", "tools", "gate")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "src", "backend")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (.github/tools/gate beside src/backend) was not found above the test binary.");
    }

    private static async Task<string> LocateBash(CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows())
        {
            return File.Exists("/bin/bash") ? "/bin/bash" : "bash";
        }

        // git --exec-path answers <git>\mingw64\libexec\git-core; git's bash is <git>\bin\bash.exe.
        var (exitCode, output) = await Run("git", ["--exec-path"], AppContext.BaseDirectory, cancellation);
        Assert.True(exitCode == 0, $"git --exec-path failed (exit {exitCode}), so git's bash cannot be located:{Environment.NewLine}{output}");
        var bash = IoPath.GetFullPath(IoPath.Combine(output.Trim(), "..", "..", "..", "bin", "bash.exe"));
        Assert.True(File.Exists(bash), $"git for Windows' bash was not found at {bash}");
        return bash;
    }

    private static async Task<(int ExitCode, string Output)> Run(string file, string[] arguments, string workingDirectory, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"'{file}' did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'{file} {string.Join(' ', arguments)}' did not finish within {Timeout}.");
        }

        return (process.ExitCode, await standardOutput + await standardError);
    }
}
