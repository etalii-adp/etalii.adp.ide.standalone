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
/// The scope is <b>the listed documents plus every in-tree readme</b> under <c>src/</c>. The
/// readmes were left out at first, as predating the documentation spec, and on 2026-09-24 that
/// exclusion was found holding five links into <c>.spec-workflow/archive</c>, which the nightly
/// cleanup had removed. They are discovered rather than listed, so a new module's readme is
/// guarded on arrival; <c>node_modules</c>, <c>bin</c> and <c>obj</c> are skipped as not the
/// repository's own writing.
/// </para>
/// <para>
/// <b><c>docs/diagrams.md</c> joined the list on 2026-09-10, and HTML links with it.</b> Until
/// then this guard read only markdown <c>[text](target)</c> links in five documents, and the
/// catalog is an HTML table of <c>&lt;a href&gt;</c> links. When archived specifications were
/// deleted from the tree, five documentation links went dead: this guard saw one, the markdown
/// link in <c>creating-an-editor-module.md</c>. The other four were catalog <c>href</c>s - three
/// of them dead for a day with the build green, because the guard checked one <i>form</i> of a
/// link and read the absence of that form as the absence of dead links.
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
        "docs/diagrams.md",
    ];

    private static string RepositoryRoot { get; } = Locate();

    /// <summary>Every <c>readme.md</c> under <c>src/</c>, repo-relative - discovered, not listed.</summary>
    private static IReadOnlyList<string> InTreeReadmes { get; } = DiscoverInTreeReadmes();

    private static IReadOnlyList<string> DiscoverInTreeReadmes()
    {
        var skipped = new HashSet<string>(["node_modules", "bin", "obj"], StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        var pending = new Stack<string>([IoPath.Combine(RepositoryRoot, "src")]);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(folder)
                .Where(file => string.Equals(IoPath.GetFileName(file), "readme.md", StringComparison.OrdinalIgnoreCase))
                .Select(file => IoPath.GetRelativePath(RepositoryRoot, file).Replace(IoPath.DirectorySeparatorChar, '/')));
            foreach (var child in Directory.EnumerateDirectories(folder).Where(child => !skipped.Contains(IoPath.GetFileName(child))))
            {
                pending.Push(child);
            }
        }

        return found;
    }

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
        var checkedLinks = 0;
        var checkedHtmlLinks = 0;

        foreach (var document in Documents.Concat(InTreeReadmes))
        {
            var fullPath = IoPath.Combine(RepositoryRoot, document);
            if (!File.Exists(fullPath))
            {
                continue; // reported by the fact above, once, rather than once per link
            }

            var folder = IoPath.GetDirectoryName(fullPath)!;
            var text = File.ReadAllText(fullPath);

            // Both forms a document in this tree links with: markdown targets, and the href and
            // src attributes of HTML written inside markdown - the catalog's table is the latter.
            var targets = LinkExpression().Matches(text).Select(match => (Html: false, Match: match))
                .Concat(HtmlLinkExpression().Matches(text).Select(match => (Html: true, Match: match)));

            foreach (var (isHtml, match) in targets)
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

                checkedLinks++;
                if (isHtml)
                {
                    checkedHtmlLinks++;
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
        // Assert, first, that links were found at all. A target the pattern misses goes unchecked
        // rather than failing wrongly, which is deliberate and documented on LinkExpression()
        // below. Whether the PATTERN still matches is asked of a fixed sample in
        // LinkExpressionReadsEveryFormTheDocumentsWrite, not counted here. This floor was 40 and
        // counted the documents, the same shape as the HTML floor below that failed a correct
        // tree on 2026-09-11 - it would have answered "do the documents still carry forty links"
        // on the first large restructure. What stays is only that there is a relative link to
        // check at all, without which the dead-link assertion is vacuous.
        Assert.True(
            checkedLinks >= 1,
            $"No relative links were extracted from {Documents.Length} delivered documents, so none are being checked at all.");

        // The HTML form, separately: the markdown count alone would stay above its floor with
        // HtmlLinkExpression() matching nothing, which is the exact blindness this pattern was
        // added to end. Whether the PATTERN still matches is asked of a fixed sample in
        // HtmlLinkExpressionReadsEveryFormTheCatalogWrites, not counted here. This floor was 5
        // and counted the documents instead: on 2026-09-11 a correct unlink of ten catalog links
        // to removed specifications left two, and it failed a right tree - it was answering "does
        // the catalog still have five links", not "has the regex stopped matching". What stays
        // is only that the catalog has a relative href at all, without which this check is vacuous.
        Assert.True(
            checkedHtmlLinks >= 1,
            $"No relative HTML links were extracted from {Documents.Length} delivered documents, so the catalog's links are not being checked at all.");

        Assert.True(dead.Count == 0, "Dead documentation links:" + Environment.NewLine + string.Join(Environment.NewLine, dead));
    }

    [Fact]
    public void TheInTreeReadmesAreFound()
    {
        // Assert. Discovery, not a count: two readmes that must always exist, one core and one
        // module, so a walk that stops finding readmes fails here instead of guarding nothing.
        Assert.Contains("src/diagrams/readme.md", InTreeReadmes);
        Assert.Contains("src/client/src/canvas/label/readme.md", InTreeReadmes);
        Assert.DoesNotContain(InTreeReadmes, readme => readme.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HtmlLinkExpressionReadsEveryFormTheCatalogWrites()
    {
        // Arrange. A fixed sample in the catalog's own shapes, so whether the pattern matches
        // does not depend on how many links the documents happen to carry today.
        const string sample =
            """<td><a href="../.spec-workflow/specs/x/requirements.md"><code>x</code></a></td>""" +
            """<img src="screenshots/canvas.png" alt="">""" +
            """<a href='../src/diagrams/y/examples/'>examples</a>""";

        // Act.
        var targets = HtmlLinkExpression().Matches(sample).Select(match => match.Groups["target"].Value).ToArray();

        // Assert.
        Assert.Equal(["../.spec-workflow/specs/x/requirements.md", "screenshots/canvas.png", "../src/diagrams/y/examples/"], targets);
    }

    [Fact]
    public void LinkExpressionReadsEveryFormTheDocumentsWrite()
    {
        // Arrange. A fixed sample in the shapes the delivered documents use - a plain link, an
        // image, a link with a title, and one with an in-page anchor - so whether the pattern
        // matches does not depend on how many links the documents happen to carry today.
        const string sample =
            "See [the readme](../readme.md). " +
            "![a screenshot](screenshots/canvas.png) " +
            "[the inventory](docs/dependencies.md \"Dependency inventory\") " +
            "[the client](creating-a-diagram-module.md#the-client)";

        // Act.
        var targets = LinkExpression().Matches(sample).Select(match => match.Groups["target"].Value).ToArray();

        // Assert.
        Assert.Equal(["../readme.md", "screenshots/canvas.png", "docs/dependencies.md", "creating-a-diagram-module.md#the-client"], targets);
    }

    /// <summary>
    /// A markdown link or image target: the parenthesised half of <c>[text](target)</c>.
    /// Markdown's own escape rules are richer, but the delivered documents use plain targets -
    /// and a target this misses simply goes unchecked rather than failing wrongly.
    /// </summary>
    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkExpression();

    /// <summary>
    /// An HTML link or image target written inside a markdown document: the quoted value of an
    /// <c>href</c> or <c>src</c> attribute. Added because <c>docs/diagrams.md</c>'s catalog is an
    /// HTML table, whose links the markdown pattern cannot see - see the remarks above.
    /// </summary>
    [GeneratedRegex(@"\b(?:href|src)\s*=\s*[""'](?<target>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkExpression();
}
