using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// Keeps <c>docs/dependencies.md</c> true (documentation spec, Requirement 9.5): its Backend and
/// Client tables are compared against the package manifests, and any row that is missing,
/// spurious or version-mismatched fails the build - with a message naming the row and the fix,
/// because a guard that only says "the document is wrong" trains people to regenerate rather
/// than think.
/// </summary>
/// <remarks>
/// <para>
/// The guard checks <b>names and versions only</b>. The document's other two columns are
/// deliberately out of scope: a reason for usage cannot be generated, and a license lookup
/// would make a unit test depend on package-metadata availability. The bargain is stated in the
/// document itself - a version bump edits its row here anyway, and re-checking the license is
/// part of that same edit.
/// </para>
/// <para>
/// Client manifests are discovered by reading the <c>workspaces</c> globs from
/// <c>src/package.json</c> and expanding them - never by scanning folders. Most module folders
/// carry no client package at all, and a folder scan would have to define "counts as a client
/// package" a second time; two definitions drift. A manifest the workspaces do not name is one
/// npm does not install for either - a repository inconsistency, not this guard's scope.
/// </para>
/// </remarks>
public class DependencyInventoryTests
{
    /// <summary>
    /// The repository root, found by walking up from the test binary - the same shape established, anchored on the two folders this
    /// guard needs.
    /// </summary>
    private static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/diagrams beside docs) was not found above the test binary.");
    }

    [Fact]
    public void TheBackendTable_MatchesDirectoryPackagesProps()
    {
        // Arrange.
        var manifest = ReadPackagesProps();
        var table = ReadTable("Backend");

        // Act and assert.
        AssertAgree("Backend", table, manifest);
    }

    [Fact]
    public void TheClientTable_MatchesTheNpmWorkspaceManifests()
    {
        // Arrange.
        var manifest = ReadWorkspaceManifests();
        var table = ReadTable("Client");

        // Act and assert.
        AssertAgree("Client", table, manifest);
    }

    [Fact]
    public void TheWorkspaceDeclaration_StillHasManifestsToRead()
    {
        // Arrange, act and assert. If the workspaces globs ever expand to nothing, the client
        // comparison above would vacuously pass against an empty document section - so the
        // discovery itself is pinned: the root, src/client, and at least one module client.
        var sources = ReadWorkspaceManifests().Values.SelectMany(entry => entry.Sources).Distinct().ToArray();
        Assert.Contains(sources, source => source.EndsWith("src/client/package.json", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Contains("/diagrams/", StringComparison.Ordinal));
    }

    // ---- the comparison -----------------------------------------------------------------------

    private static void AssertAgree(
        string section,
        IReadOnlyDictionary<string, string> table,
        IReadOnlyDictionary<string, DependencyManifestEntry> manifest)
    {
        var problems = new List<string>();

        foreach (var (name, entry) in manifest.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!table.TryGetValue(name, out var recorded))
            {
                problems.Add(
                    $"The {section} table is missing '{name}' ({entry.Specifier}, from {entry.SourceList}). " +
                    "Add a row - and write its reason and license, they cannot be generated.");
            }
            else if (recorded != entry.Specifier)
            {
                problems.Add(
                    $"The {section} table records '{name}' at '{recorded}' but {entry.SourceList} says '{entry.Specifier}'. " +
                    "Update the row and re-check its license.");
            }
        }

        foreach (var name in table.Keys.Where(name => !manifest.ContainsKey(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add(
                $"The {section} table lists '{name}', which no manifest references any more. " +
                "Remove the row, or restore the package.");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ---- the manifests ------------------------------------------------------------------------

    private static Dictionary<string, DependencyManifestEntry> ReadPackagesProps()
    {
        var path = IoPath.Combine(RepositoryRoot, "src", "Directory.Packages.props");
        var entries = new Dictionary<string, DependencyManifestEntry>(StringComparer.Ordinal);

        foreach (var element in XDocument.Load(path).Descendants("PackageVersion"))
        {
            var name = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value;
            if (name is null || version is null)
            {
                continue;
            }

            entries[name] = new DependencyManifestEntry(version, ["src/Directory.Packages.props"]);
        }

        return entries;
    }

    private static Dictionary<string, DependencyManifestEntry> ReadWorkspaceManifests()
    {
        var root = IoPath.Combine(RepositoryRoot, "src", "package.json");
        var manifests = new List<string> { root };

        using (var workspace = JsonDocument.Parse(File.ReadAllText(root)))
        {
            if (workspace.RootElement.TryGetProperty("workspaces", out var globs))
            {
                foreach (var glob in globs.EnumerateArray())
                {
                    manifests.AddRange(Expand(glob.GetString() ?? ""));
                }
            }
        }

        // name -> specifier -> the manifests that state it. A package whose manifests disagree
        // is recorded with every distinct specifier, sorted and comma-joined, so divergence is
        // visible in the document rather than hidden by whichever manifest was read last.
        var collected = new Dictionary<string, SortedDictionary<string, List<string>>>(StringComparer.Ordinal);

        foreach (var manifest in manifests.Where(File.Exists))
        {
            var relative = IoPath.GetRelativePath(RepositoryRoot, manifest).Replace('\\', '/');
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));

            foreach (var kind in new[] { "dependencies", "devDependencies" })
            {
                if (!document.RootElement.TryGetProperty(kind, out var dependencies))
                {
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    var bySpecifier = collected.TryGetValue(dependency.Name, out var known)
                        ? known
                        : collected[dependency.Name] = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

                    var sources = bySpecifier.TryGetValue(dependency.Value.GetString() ?? "", out var list)
                        ? list
                        : bySpecifier[dependency.Value.GetString() ?? ""] = [];

                    sources.Add(relative);
                }
            }
        }

        return collected.ToDictionary(
            pair => pair.Key,
            pair => new DependencyManifestEntry(
                string.Join(", ", pair.Value.Keys),
                pair.Value.Values.SelectMany(sources => sources).Distinct().ToArray()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Expands one npm workspaces glob against the filesystem. The two shapes this repository
    /// uses - a literal folder and a one-level <c>*</c> segment - are supported; anything fancier
    /// arriving in <c>src/package.json</c> should extend this deliberately rather than silently
    /// matching nothing.
    /// </summary>
    private static IEnumerable<string> Expand(string glob)
    {
        var sourceFolder = IoPath.Combine(RepositoryRoot, "src");
        if (!glob.Contains('*', StringComparison.Ordinal))
        {
            yield return IoPath.Combine(sourceFolder, glob.Replace('/', IoPath.DirectorySeparatorChar), "package.json");
            yield break;
        }

        var segments = glob.Split('/');
        var starIndex = Array.IndexOf(segments, "*");
        if (starIndex < 0)
        {
            throw new InvalidOperationException($"The workspaces glob '{glob}' uses a pattern this guard does not support; extend Expand() deliberately.");
        }

        var prefix = IoPath.Combine([sourceFolder, .. segments[..starIndex]]);
        if (!Directory.Exists(prefix))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(prefix))
        {
            yield return IoPath.Combine([folder, .. segments[(starIndex + 1)..], "package.json"]);
        }
    }

    // ---- the document -------------------------------------------------------------------------

    /// <summary>
    /// The name and version columns of one section's table: rows between that section's
    /// <c>##</c> heading and the next, split on <c>|</c>, backticks stripped, header and
    /// separator rows skipped.
    /// </summary>
    private static Dictionary<string, string> ReadTable(string section)
    {
        var path = IoPath.Combine(RepositoryRoot, "docs", "dependencies.md");
        Assert.True(File.Exists(path), $"docs/dependencies.md does not exist, so the {section} table cannot be checked.");

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var inSection = false;

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = string.Equals(line[3..].Trim(), section, StringComparison.Ordinal);
                continue;
            }

            if (!inSection || !line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries);

            // Split on a leading and trailing pipe yields empty first/last cells; a real row has
            // the name at index 1 and the version at index 2.
            if (cells.Length < 3)
            {
                continue;
            }

            var name = cells[1].Trim('`').Trim();
            var version = cells[2].Trim();

            if (name.Length == 0 || name == "Dependency" || name.All(character => character == '-'))
            {
                continue;
            }

            rows[name] = version;
        }

        Assert.True(rows.Count > 0, $"No rows were parsed under the '## {section}' heading of docs/dependencies.md - if the table moved or was reshaped, this guard's parser needs the same change.");
        return rows;
    }
}
