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
/// Green needs both halves of the script's answer: exit code 0 <b>and</b> its
/// <c>RESULT=selftest-green</c> line, which it prints only when every expected case ran and none
/// was wrong.
/// </para>
/// </remarks>
public class GateScriptTests
{
    // A LIVENESS BOUND, NOT A PERFORMANCE BUDGET. The suite drives a few hundred git processes, and
    // what that costs has moved a long way: 35 s on a quiet machine and 209 s on a slow one on
    // 2026-09-22, then 1274 s - twenty-one minutes - on a QUIET machine on 2026-09-23, measured
    // alone with nothing else running. The 15-minute ceiling set against the first two numbers was
    // therefore being exceeded by every gate on the board, deterministically and for a reason no
    // branch could fix, because a branch carrying the remedy must itself pass this test.
    //
    // Raised to 45 rather than to just above 21 for two reasons. A ceiling set at the last
    // measurement is already too low for the next landing - the self-test is 167 cases here and a
    // branch taking it to 177 is in hand. And a timeout doing double duty as a performance alarm
    // stops the board every time the honest number drifts, which is what just happened.
    //
    // WHAT IS NOT MEASURED, stated rather than implied: the 1274 s is a QUIET-MACHINE figure. Under
    // a full gate this test ran past 15 minutes and was killed there, so its loaded cost is unknown
    // and 45 minutes is 2.1x an unloaded number rather than a headroom over a measured one. If this
    // fires again, that ratio is the first thing to measure and the growth is the thing to fix.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(45);

    [Fact]
    public async Task SelfTestIsGreen()
    {
        // Arrange.
        var cancellation = TestContext.Current.CancellationToken;
        var root = LocateRepositoryRoot();
        var script = IoPath.Combine(root, ".github", "tools", "gate", "gate.test.sh");
        Assert.True(File.Exists(script), $"The shared gate's self-test is missing: {script}");
        var bash = await LocateBash(cancellation);

        // Act.
        var (exitCode, output) = await Run(bash, [script.Replace('\\', '/')], root, cancellation);

        // Assert.
        Assert.True(
            exitCode == 0 && output.Contains("RESULT=selftest-green", StringComparison.Ordinal),
            $"The shared gate's self-test is not green (exit {exitCode}):{Environment.NewLine}{output}");
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
