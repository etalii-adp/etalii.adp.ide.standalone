using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Every relative link and image reference in the delivered documentation resolves to a file
/// that exists (documentation spec, NFR Reliability): the root readme, the dependency
/// inventory, the two module walkthroughs and the screenshots procedure. A front page whose
/// link lands on a 404 fails the build, not a reader.
/// </summary>
/// <remarks>
/// <para>
/// The scope is <b>exactly the delivered list</b>, not a repository-wide crawl:
/// <c>docs/diagrams.md</c> and the module readmes predate the documentation spec and are not
/// retro-guarded here. Widening the net is a later decision, deliberately not smuggled into
/// this guard.
/// </para>
/// <para>
/// Absolute URLs are ignored, not fetched - no network in tests - and in-page anchors are the
/// renderer's business; this guard answers only whether the FILE half of every link exists.
/// </para>
/// </remarks>
public partial class DocumentationLinksTests
{
    /// <summary>The delivered documents, repo-relative.</summary>
    private static readonly string[] Documents =
    [
        "readme.md",
        "docs/dependencies.md",
        "docs/creating-a-diagram-module.md",
        "docs/creating-an-editor-module.md",
        "docs/screenshots/readme.md",
    ];

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
    public void EveryDeliveredDocumentExists()
    {
        // Act and assert. The list itself is part of the contract: a delivered document that
        // vanishes should fail loudly here rather than silently shrinking the guarded set.
        foreach (var document in Documents)
        {
            Assert.True(
                File.Exists(IoPath.Combine(RepositoryRoot, document)),
                $"'{document}' is a delivered document and does not exist - if it moved, move it in this guard's list too.");
        }
    }

    [Fact]
    public void EveryRelativeLinkResolves()
    {
        // Arrange.
        var dead = new List<string>();

        foreach (var document in Documents)
        {
            var fullPath = IoPath.Combine(RepositoryRoot, document);
            if (!File.Exists(fullPath))
            {
                continue; // reported by the fact above, once, rather than once per link
            }

            var folder = IoPath.GetDirectoryName(fullPath)!;
            foreach (Match match in LinkExpression().Matches(File.ReadAllText(fullPath)))
            {
                var target = match.Groups["target"].Value.Trim();

                // Not this guard's business: the web, mail, and pure in-page anchors.
                if (target.Length == 0 ||
                    target.StartsWith('#') ||
                    target.Contains("://", StringComparison.Ordinal) ||
                    target.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    continue;
                }

                var file = Uri.UnescapeDataString(target.Split('#')[0]);
                if (file.Length == 0)
                {
                    continue;
                }

                var resolved = IoPath.GetFullPath(IoPath.Combine(folder, file.Replace('/', IoPath.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    dead.Add($"{document} -> {target}");
                }
            }
        }

        // Assert. Every dead link at once, each naming its source, so one run gives the whole
        // repair list.
        Assert.True(dead.Count == 0, "Dead documentation links:" + Environment.NewLine + string.Join(Environment.NewLine, dead));
    }

    /// <summary>
    /// A markdown link or image target: the parenthesised half of <c>[text](target)</c>.
    /// Markdown's own escape rules are richer, but the delivered documents use plain targets -
    /// and a target this misses simply goes unchecked rather than failing wrongly.
    /// </summary>
    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkExpression();
}
