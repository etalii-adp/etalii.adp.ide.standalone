using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Keeps <c>src/package-lock.json</c> agreeing with the npm workspaces. When a module gains a
/// client <c>package.json</c> without the lock being regenerated, <c>npm ci</c> refuses to
/// install and CI goes red before any gate step runs - which is exactly how the databricks
/// client's manifest (c44945ad) broke the Build workflow for several runs before 2f4a6a94
/// restored the lock. The four local gates never run <c>npm ci</c> (an existing
/// <c>node_modules</c> serves <c>npm test</c> and the typecheck just fine), so the local
/// discipline had a blind spot exactly the size of this bug; this guard closes it from the
/// ordinary backend gate.
/// </summary>
/// <remarks>
/// Both directions matter: a manifest missing from the lock breaks the strict install, and a
/// removed module leaves a stale lock entry that does the same. Workspace manifests are
/// discovered by expanding the <c>workspaces</c> globs from <c>src/package.json</c> - the same
/// bargain <see cref="DependencyInventoryTests"/> states: never by folder scan, because a
/// second definition of "counts as a client package" would drift from npm's own.
/// </remarks>
public class WorkspaceLockTests
{
    /// <summary>
    /// The repository root, found by walking up from the test binary - the shape established and <see cref="DependencyInventoryTests"/>
    /// reuses, anchored on the folders this guard needs.
    /// </summary>
    private static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                File.Exists(IoPath.Combine(directory.FullName, "src", "package-lock.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/diagrams beside src/package-lock.json) was not found above the test binary.");
    }

    [Fact]
    public void TheWalk_StillFindsTheWorkspaces()
    {
        // Arrange and act.
        var workspaces = WorkspaceManifestFolders();

        // Assert: if the globs ever expand to nothing, the comparison below would vacuously
        // pass an empty set against an empty set - so the discovery itself is pinned: the
        // shell client and at least one module client must be found.
        Assert.Contains("client", workspaces);
        Assert.Contains(workspaces, workspace => workspace.StartsWith("diagrams/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLockFile_RecordsExactlyTheWorkspaceManifests()
    {
        // Arrange.
        var workspaces = WorkspaceManifestFolders();
        var recorded = LockedWorkspaceFolders();

        // Act.
        var missing = workspaces.Except(recorded, StringComparer.Ordinal).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
        var stale = recorded.Except(workspaces, StringComparer.Ordinal).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();

        // Assert.
        // The message names the offending manifests and the fix: a guard that only says "the
        // lock is wrong" trains people to regenerate blindly.
        var problems = new List<string>();
        problems.AddRange(missing.Select(entry =>
            $"src/package-lock.json has no workspace entry for src/{entry}/package.json - " +
            "the manifest joined the workspaces without the lock being regenerated, and `npm ci` will refuse to install. " +
            "Run `npm install` in src/ and commit the updated lock."));
        problems.AddRange(stale.Select(entry =>
            $"src/package-lock.json still records the workspace 'src/{entry}', whose package.json is gone - " +
            "a stale entry breaks `npm ci` just as surely. " +
            "Run `npm install` in src/ and commit the updated lock."));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The workspace folders npm would install for: the <c>workspaces</c> globs of
    /// <c>src/package.json</c>, expanded to the folders whose <c>package.json</c> actually
    /// exists, as src-relative forward-slash paths (the lock's own key format).
    /// </summary>
    private static IReadOnlyList<string> WorkspaceManifestFolders()
    {
        var sourceFolder = IoPath.Combine(RepositoryRoot, "src");
        var folders = new List<string>();

        using var manifest = JsonDocument.Parse(File.ReadAllText(IoPath.Combine(sourceFolder, "package.json")));
        if (!manifest.RootElement.TryGetProperty("workspaces", out var globs))
        {
            return folders;
        }

        foreach (var element in globs.EnumerateArray())
        {
            var glob = element.GetString() ?? "";
            if (!glob.Contains('*', StringComparison.Ordinal))
            {
                if (File.Exists(IoPath.Combine(sourceFolder, glob.Replace('/', IoPath.DirectorySeparatorChar), "package.json")))
                {
                    folders.Add(glob);
                }

                continue;
            }

            // The one pattern the repository uses: a single '*' directory segment
            // ("diagrams/*/client"). Anything fancier fails loudly rather than expanding wrong.
            var segments = glob.Split('/');
            var starIndex = Array.IndexOf(segments, "*");
            if (starIndex < 0 || segments.Count(segment => segment.Contains('*', StringComparison.Ordinal)) != 1)
            {
                throw new InvalidOperationException($"The workspaces glob '{glob}' uses a pattern this guard does not support; extend it deliberately.");
            }

            var prefix = IoPath.Combine([sourceFolder, .. segments[..starIndex]]);
            if (!Directory.Exists(prefix))
            {
                continue;
            }

            foreach (var candidate in Directory.EnumerateDirectories(prefix).OrderBy(path => path, StringComparer.Ordinal))
            {
                var expanded = (string[])[.. segments[..starIndex], IoPath.GetFileName(candidate), .. segments[(starIndex + 1)..]];
                if (File.Exists(IoPath.Combine([sourceFolder, .. expanded, "package.json"])))
                {
                    folders.Add(string.Join('/', expanded));
                }
            }
        }

        return folders;
    }

    /// <summary>
    /// The workspace folders the lock records: every key of its <c>packages</c> object that is
    /// neither the root (<c>""</c>) nor an installed dependency (<c>node_modules/...</c>).
    /// </summary>
    private static IReadOnlyList<string> LockedWorkspaceFolders()
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(IoPath.Combine(RepositoryRoot, "src", "package-lock.json")));
        return
        [
            .. lockFile.RootElement.GetProperty("packages").EnumerateObject()
                .Select(package => package.Name)
                .Where(name => name.Length > 0 && !name.StartsWith("node_modules/", StringComparison.Ordinal)),
        ];
    }
}
