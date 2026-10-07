using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// Every declared namespace under the backend's non-test projects matches the project name
/// plus its unskipped folder segments, with the skip entries read from each project's own
/// <c>.csproj.DotSettings</c> - escaping decoded, so a mis-escaped key (which parses fine
/// and matches nothing) surfaces as a mismatch here instead of passing silently.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists as a test.</b> This drift is what <c>jb inspectcode</c> uniquely sees
/// over <c>dotnet format</c>, and the backend-project-decomposition tasks require that check
/// per landing - but ReSharper.GlobalTools 2026.2.1 cannot evaluate SDK 10.0.203 projects
/// (MSB4236 on the workload locator; MSB4018/FileNotFound in ProcessFrameworkReferences;
/// project evaluation fails entirely under --no-build). This committed guard is the ruled
/// substitute (Architect 1, task 3): it runs on every gate identically, and real
/// inspectcode returns the moment a jb release evaluates this SDK.
/// </para>
/// <para>
/// <b>Scope.</b> Non-test projects under <c>src/backend</c>, discovered rather than listed,
/// so the decomposition's new area projects join the guard by existing. Test projects keep
/// flat namespaces over foldered files by convention and are out of scope. A file may opt
/// out the way inspectcode allows: a <c>ReSharper disable ... CheckNamespace</c> comment
/// (ShortGuid.Cast.cs uses it - a generated-type partial must sit in the generated
/// namespace regardless of its folder).
/// </para>
/// <para>
/// <b>Seen to fail</b> (backend-project-decomposition task 3): mis-escaping one skip key
/// (<c>context_005C_005Fmodel</c> to <c>context_005Cmodel</c>) turned exactly the
/// Context/_Model files into thirty named mismatches; reverting restored green.
/// </para>
/// </remarks>
public class NamespaceProvidersTests
{
    private static readonly Regex SkipEntry = new(
        """NamespaceFoldersToSkip/=([^/@]+)/@EntryIndexedValue">True<""", RegexOptions.Compiled);

    private static readonly Regex DeclaredNamespace = new(@"^namespace ([\w.]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Suppressed = new("ReSharper disable( once)? CheckNamespace", RegexOptions.Compiled);

    [Fact]
    public void EveryBackendNamespaceFollowsItsFoldersMinusTheDeclaredSkips()
    {
        var backend = BackendSourceRoot();
        var mismatches = new List<string>();

        foreach (var projectDirectory in Directory.EnumerateDirectories(backend))
        {
            var project = IoPath.GetFileName(projectDirectory);
            if (project.EndsWith(".Tests", StringComparison.Ordinal)
                || !File.Exists(IoPath.Combine(projectDirectory, project + ".csproj")))
            {
                continue;
            }

            var skips = SkippedFolders(IoPath.Combine(projectDirectory, project + ".csproj.DotSettings"));
            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = IoPath.GetRelativePath(projectDirectory, file);
                if (relative.StartsWith("obj", StringComparison.Ordinal) || relative.StartsWith("bin", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (Suppressed.IsMatch(text))
                {
                    continue;
                }

                var declared = DeclaredNamespace.Match(text);
                if (!declared.Success)
                {
                    continue;
                }

                var expected = ExpectedNamespace(project, relative, skips);
                if (declared.Groups[1].Value != expected)
                {
                    mismatches.Add($"{project}/{relative}: declares {declared.Groups[1].Value}, expected {expected}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>The skip keys, unescaped: `_005C` is a path separator, `_005F` an underscore.</summary>
    private static HashSet<string> SkippedFolders(string dotSettingsPath)
    {
        var skips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(dotSettingsPath))
        {
            return skips;
        }

        foreach (Match match in SkipEntry.Matches(File.ReadAllText(dotSettingsPath)))
        {
            skips.Add(match.Groups[1].Value.Replace("_005C", "\\").Replace("_005F", "_"));
        }

        return skips;
    }

    private static string ExpectedNamespace(string project, string relativeFilePath, HashSet<string> skips)
    {
        var folders = IoPath.GetDirectoryName(relativeFilePath)?.Split(IoPath.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ?? [];
        var kept = new List<string> { project };
        for (var i = 0; i < folders.Length; i++)
        {
            var pathSoFar = string.Join("\\", folders[..(i + 1)]);
            if (skips.Contains(folders[i]) || skips.Contains(pathSoFar))
            {
                continue;
            }

            kept.Add(folders[i]);
        }

        return string.Join(".", kept);
    }

    /// <summary>The src/backend folder, found by walking up - the family's shared idiom.</summary>
    private static string BackendSourceRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var depth = 0; depth < 12; depth++)
        {
            var candidate = IoPath.Combine(directory, "src", "backend");
            if (File.Exists(IoPath.Combine(candidate, "EtAlii.Adp.slnx")))
            {
                return candidate;
            }

            directory = IoPath.GetDirectoryName(directory) ?? throw new InvalidOperationException("src/backend was not found above the test assembly.");
        }

        throw new InvalidOperationException("src/backend was not found above the test assembly.");
    }
}
