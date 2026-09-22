using System.Diagnostics;
using System.Text.Json;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Client.Tests;

/// <summary>One test file's outcome, as vitest reported it.</summary>
/// <param name="Path">The file, repository-relative with forward slashes - the name a case carries.</param>
/// <param name="Status">vitest's own word: <c>passed</c>, <c>failed</c>, or occasionally <c>skipped</c>.</param>
/// <param name="Message">What vitest said when the file itself could not run - a collection error.</param>
/// <param name="Tests">Every individual test in the file.</param>
public sealed record ClientTestFile(string Path, string Status, string Message, IReadOnlyList<ClientTest> Tests);

/// <summary>One individual test inside a file.</summary>
/// <param name="File">The file it lives in, repository-relative.</param>
/// <param name="FullName">Its describe-chain and name, as vitest prints it.</param>
/// <param name="Status">vitest's own word: <c>passed</c>, <c>failed</c>, <c>skipped</c> or <c>todo</c>.</param>
/// <param name="FailureMessages">vitest's assertion output, verbatim, when it failed.</param>
public sealed record ClientTest(string File, string FullName, string Status, IReadOnlyList<string> FailureMessages);

/// <summary>
/// The client's vitest suite, run ONCE per process and read as structured results.
/// </summary>
/// <remarks>
/// <para>
/// <b>One run, not one per file.</b> Measured on this tree: the whole suite is ~10s, a single file
/// ~2s, so a run per file would cost ~4.5 minutes to report exactly what one run already knows.
/// The run is therefore shared - <see cref="Result"/> is lazy and thread-safe - and every case reads
/// its own verdict out of it.
/// </para>
/// <para>
/// <b>vitest is asked for JSON</b> (<c>--reporter=json</c>), which carries per-file status and, inside
/// each file, every test with its status and failure messages. That is what lets the .NET suite
/// report the client suite test by test instead of as one opaque pass or fail.
/// </para>
/// <para>
/// <b>`npx vitest run`, not `npm test`.</b> `npm test` would first run `npm run generate`, which
/// rewrites the generated client sources - a side effect a test must not have on a working tree.
/// The gate runs `npm test` before this project is ever built, so the generated sources are present;
/// when they are not, vitest says so and the cases fail carrying its words (see <see cref="Failure"/>).
/// </para>
/// </remarks>
public static class ClientTestRun
{
    private static readonly Lazy<ClientRunResult> Lazy = new(Run, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The shared run. The first caller pays for it; everybody else reads it.</summary>
    public static ClientRunResult Result => Lazy.Value;

    /// <summary>The client folder, found by walking up from the test binary.</summary>
    public static string ClientRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "client");
            if (File.Exists(IoPath.Combine(candidate, "package.json")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"No src/client/package.json above {AppContext.BaseDirectory}.");
    }

    /// <summary>The repository root - the folder src/ sits in.</summary>
    public static string RepositoryRoot() => IoPath.GetFullPath(IoPath.Combine(ClientRoot(), "..", ".."));

    /// <summary>
    /// Every <c>*.test.ts</c> and <c>*.test.tsx</c> in the client and in every diagram module's client,
    /// repository-relative with forward slashes. <b>Read from the filesystem</b>, never from the run, so
    /// a file the run never opened is still a case - and fails, rather than vanishing.
    /// </summary>
    public static IReadOnlyList<string> TestFiles()
    {
        var root = RepositoryRoot();
        var roots = new[] { IoPath.Combine(root, "src", "client", "src"), IoPath.Combine(root, "src", "diagrams") };
        return
        [
            .. roots
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.test.ts?", SearchOption.AllDirectories))
                .Where(file => file.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase))
                .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}node_modules{IoPath.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(file => Relative(root, file))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string Relative(string root, string path) =>
        IoPath.GetRelativePath(root, path).Replace('\\', '/');

    private static ClientRunResult Run()
    {
        var client = ClientRoot();
        // The install lives at the npm WORKSPACE root, src/, with a per-workspace folder beside each
        // package; either being absent means nothing can run. Checked before spawning anything, so the
        // reason is a sentence rather than a module-resolution stack.
        var workspace = IoPath.GetFullPath(IoPath.Combine(client, ".."));
        var missing = new[] { IoPath.Combine(workspace, "node_modules"), IoPath.Combine(client, "node_modules") }
            .Where(folder => !Directory.Exists(folder))
            .ToList();
        if (missing.Count > 0)
        {
            // Stated rather than skipped: a green suite that silently tested nothing is the one
            // outcome worse than a red one. `npm install` in src/ is the fix, and the gate does it.
            return ClientRunResult.NotRun(
                "The client's dependencies are not installed, so its tests could not run. Missing: "
                + string.Join(", ", missing)
                + ". Run `npm install` in src/ - the gate does this before it tests.");
        }

        var report = IoPath.Combine(IoPath.GetTempPath(), "adp-client-vitest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exitCode, output) = Execute(client, report);
            if (!File.Exists(report))
            {
                return ClientRunResult.NotRun($"vitest wrote no report (exit code {exitCode}). Its output was:{Environment.NewLine}{output}");
            }

            var files = Parse(File.ReadAllText(report), RepositoryRoot());
            return new ClientRunResult(files, exitCode, output, null);
        }
        catch (Exception exception)
        {
            return ClientRunResult.NotRun($"The client test run could not be driven: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (File.Exists(report))
            {
                File.Delete(report);
            }
        }
    }

    private static (int ExitCode, string Output) Execute(string client, string report)
    {
        // npx through the shell on Windows: npx is a .cmd, which Process cannot start directly.
        var windows = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo
        {
            FileName = windows ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = client,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var command = $"npx vitest run --reporter=json --outputFile=\"{report}\"";
        start.ArgumentList.Add(windows ? "/c" : "-c");
        start.ArgumentList.Add(command);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start: {command}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // CAPPED, because this runs inside a gate. The measured run is ~10s; a vitest that hangs -
        // a watch flag slipping in, a test awaiting something that never happens - would otherwise
        // hold the whole backend suite open with no output and no verdict. Ten minutes is sixty
        // times the measurement, so it can only be reached by a hang, and it fails saying so.
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return (-1, $"vitest did not finish within {Timeout.TotalMinutes:0} minutes and was stopped. It had printed:{Environment.NewLine}{Read(stdout)}{Environment.NewLine}{Read(stderr)}");
        }

        return (process.ExitCode, (Read(stdout) + Environment.NewLine + Read(stderr)).Trim());
    }

    /// <summary>How long a client run may take before it is treated as hung. See <see cref="Execute"/>.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>Whatever the stream produced, without waiting on a process that is already gone.</summary>
    private static string Read(Task<string> stream)
    {
        try
        {
            return stream.Wait(TimeSpan.FromSeconds(10)) ? stream.Result : "";
        }
        catch (AggregateException)
        {
            return "";
        }
    }

    private static IReadOnlyList<ClientTestFile> Parse(string json, string root)
    {
        using var document = JsonDocument.Parse(json);
        var files = new List<ClientTestFile>();
        if (!document.RootElement.TryGetProperty("testResults", out var results))
        {
            return files;
        }

        foreach (var file in results.EnumerateArray())
        {
            var path = Relative(root, file.GetProperty("name").GetString() ?? "");
            var tests = new List<ClientTest>();
            if (file.TryGetProperty("assertionResults", out var assertions))
            {
                foreach (var assertion in assertions.EnumerateArray())
                {
                    tests.Add(new ClientTest(
                        path,
                        assertion.GetProperty("fullName").GetString() ?? "",
                        assertion.GetProperty("status").GetString() ?? "",
                        assertion.TryGetProperty("failureMessages", out var messages)
                            ? [.. messages.EnumerateArray().Select(message => message.GetString() ?? "")]
                            : []));
                }
            }

            files.Add(new ClientTestFile(
                path,
                file.GetProperty("status").GetString() ?? "",
                file.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "",
                tests));
        }

        return files;
    }
}

/// <summary>What one shared vitest run produced, or why there was none.</summary>
/// <param name="Files">Every file vitest reported.</param>
/// <param name="ExitCode">vitest's own exit code.</param>
/// <param name="Output">Everything vitest printed - the evidence a failing case carries.</param>
/// <param name="Failure">Why no run happened at all; null when one did.</param>
public sealed record ClientRunResult(IReadOnlyList<ClientTestFile> Files, int ExitCode, string Output, string? Failure)
{
    /// <summary>A run that could not happen, carrying the reason every case will fail with.</summary>
    public static ClientRunResult NotRun(string reason) => new([], -1, "", reason);

    /// <summary>The file's outcome, or null when vitest never reported it.</summary>
    public ClientTestFile? File(string path) =>
        Files.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
}
