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
