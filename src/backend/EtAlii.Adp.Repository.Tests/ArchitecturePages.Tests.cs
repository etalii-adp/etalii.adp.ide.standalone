using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// The two architecture pages still describe this tree: every path they name exists, every project
/// they name is in the solution, and every count they state recomputes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Modelled on <see cref="GuardInventoryTests"/></b>, which does the same job for
/// <c>docs/guards.md</c>: locate the repository root, parse the document, assert, and carry a floor
/// so an emptied page fails rather than passing vacuously.
/// </para>
/// <para>
/// <b>What it cannot do, and the pages say so themselves.</b> It checks existence, counts and size.
/// It cannot check whether a DESCRIPTION is still accurate. The claim these pages were written to
/// end - that the canvas is drawn by Konva or PixiJS - would have passed every assertion here,
/// because <c>src/client/package.json</c> exists and the sentence stated no count. A guard that is
/// trusted beyond its reach is worse than none, so its reach is written down.
/// </para>
/// <para>
/// <b>Counts are asserted on the two pages and NOT in the agent files, deliberately.</b> A steering
/// document's count is usually a DATED MEASUREMENT - "measured on 2026-09-22 over 1430 .cs files" -
/// which records what was true THEN and stays true as a record after the tree moves. A page's count
/// is a claim about NOW. Only a claim about now can be recomputed, and asserting the dated ones
/// would redden the build for a document being honest about its own history - where the only way
/// back to green is deleting a true sentence. So: paths and project names everywhere, counts only
/// on the pages. A steering document wanting a live count references the page instead.
/// </para>
/// </remarks>
public partial class ArchitecturePagesTests
{
    private const string ArchitecturePage = "docs/architecture.md";
    private const string StructurePage = "docs/solution-structure.md";

    /// <summary>At most, per page (Requirement 7.2).</summary>
    private const int LineLimit = 150;

    private const int DiagramLimit = 3;

    /// <summary>
    /// Floors deliberately BELOW the actual, so an ordinary edit does not touch them while an
    /// emptied or reshaped page does. An exact count would be a second copy of the page.
    /// </summary>
    private const int PathFloor = 4;

    private const int CountFloor = 4;

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

    private static string Read(string relativePath) =>
        File.ReadAllText(IoPath.Combine(RepositoryRoot, relativePath.Replace('/', IoPath.DirectorySeparatorChar)));

    /// <summary>
    /// A backticked repo-relative path: one that STARTS at a top-level directory of this
    /// repository.
    /// </summary>
    /// <remarks>
    /// The anchor is what makes the rule precise rather than merely strict. Both pages also
    /// backtick strings that contain a slash and are deliberately not repo paths - a solution entry
    /// quoted to show it has no <c>backend</c> segment, a folder fragment shown to explain a
    /// namespace skip. Those are illustrations, not claims that a file exists, and a guard that
    /// reddened on them would be teaching the page to stop explaining itself.
    /// </remarks>
    [GeneratedRegex(@"`((?:src|docs|\.spec-workflow|\.github)/[A-Za-z0-9_./\- ]+)`")]
    private static partial Regex RepoPathExpression();

    /// <summary>An <c>EtAlii.Adp.*</c> project name, as the pages and the agent files write it.</summary>
    [GeneratedRegex(@"`?\b(EtAlii\.Adp(?:\.[A-Za-z0-9]+)*)\b`?")]
    private static partial Regex ProjectNameExpression();

    /// <summary>A stated count, in the fixed form the pages use: <c>**105**</c>.</summary>
    [GeneratedRegex(@"\*\*(\d+)\*\*")]
    private static partial Regex CountExpression();

    private static string[] PathsIn(string relativePath) =>
        RepoPathExpression()
            .Matches(Read(relativePath))
            .Select(match => match.Groups[1].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Segments that make a dotted <c>EtAlii.Adp.*</c> string a FILE NAME rather than a project
    /// name - <c>EtAlii.Adp.slnx</c>, <c>EtAlii.Adp.Context.csproj</c>,
    /// <c>EtAlii.Adp.Backend.csproj.DotSettings</c>.
    /// </summary>
    /// <remarks>
    /// Found by running this guard before trusting it: all three documents "failed" on file names
    /// the first time, which is the pattern being wrong rather than the documents. A guard whose
    /// first red is its own false positive has to be fixed before its reds mean anything.
    /// </remarks>
    private static readonly string[] FileNameSegments =
        ["slnx", "sln", "csproj", "cs", "md", "json", "props", "targets", "DotSettings", "editorconfig"];

    private static string[] ProjectNamesIn(string relativePath) =>
        ProjectNameExpression()
            .Matches(Read(relativePath))
            .Select(match => match.Groups[1].Value)
            .Where(name => !name.Split('.').Any(segment => FileNameSegments.Contains(segment, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // ---- the tree, recomputed ------------------------------------------------------------------

    private static string[] SolutionProjectPaths()
    {
        var solution = Read("src/backend/EtAlii.Adp.slnx");
        return Regex.Matches(solution, @"<Project Path=""([^""]+)""")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .ToArray();
    }

    /// <summary>Every project name in the solution, by its file name.</summary>
    private static HashSet<string> SolutionProjectNames() =>
        SolutionProjectPaths()
            .Select(path => IoPath.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.Ordinal);

    private static int TrackedProjectFiles()
    {
        // Enumerated rather than globbed: a glob over this tree has already produced a confidently
        // wrong answer, and one directory here is literally named "example 1".
        var source = IoPath.Combine(RepositoryRoot, "src");
        return Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories)
            .Count(path => !path.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>The counts each page states, by the label that follows them, recomputed from the tree.</summary>
    private static Dictionary<string, int> ExpectedCounts()
    {
        var paths = SolutionProjectPaths();
        var core = paths.Count(path => !path.StartsWith("../", StringComparison.Ordinal));
        var diagram = paths.Count(path => path.StartsWith("../diagrams/", StringComparison.Ordinal));
        var editor = paths.Count(path => path.StartsWith("../editors/", StringComparison.Ordinal));
        var designer = paths.Count(path => path.StartsWith("../designers/", StringComparison.Ordinal));
        var test = paths.Count(path => path.EndsWith(".Tests.csproj", StringComparison.Ordinal));
        var tracked = TrackedProjectFiles();

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["projects in the solution"] = paths.Length,
            ["core"] = core,
            ["diagram"] = diagram,
            ["editor"] = editor,
            ["designer"] = designer,
            ["production"] = paths.Length - test,
            ["test"] = test,
            ["tracked project files"] = tracked,
            ["the difference"] = tracked - paths.Length,
            ["core production"] = paths.Count(path => !path.StartsWith("../", StringComparison.Ordinal)
                                                     && !path.EndsWith(".Tests.csproj", StringComparison.Ordinal)),
            ["core test"] = paths.Count(path => !path.StartsWith("../", StringComparison.Ordinal)
                                                && path.EndsWith(".Tests.csproj", StringComparison.Ordinal)),
        };
    }

    // ---- the assertions -----------------------------------------------------------------------

    [Theory]
    [InlineData(ArchitecturePage)]
    [InlineData(StructurePage)]
    public void EveryPathAPageNames_Exists(string page) => AssertPathsExist(page);

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData(".spec-workflow/steering/structure.md")]
    [InlineData(".spec-workflow/steering/tech.md")]
    [InlineData(".spec-workflow/steering/product.md")]
    [InlineData(".spec-workflow/steering/roles.md")]
    public void EveryPathAnAgentFileNames_Exists(string file) => AssertPathsExist(file);

    private static void AssertPathsExist(string document)
    {
        var missing = PathsIn(document)
            .Where(path =>
            {
                var full = IoPath.Combine(RepositoryRoot, path.Replace('/', IoPath.DirectorySeparatorChar));
                return !File.Exists(full) && !Directory.Exists(full);
            })
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{document} names {missing.Length} repo-relative path(s) that do not exist. Either the file moved and "
            + "this document must follow it, or the path was never right:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Theory]
    [InlineData(ArchitecturePage)]
    [InlineData(StructurePage)]
    public void EveryProjectAPageNames_IsInTheSolution(string page) => AssertProjectNames(page);

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData(".spec-workflow/steering/structure.md")]
    [InlineData(".spec-workflow/steering/tech.md")]
    [InlineData(".spec-workflow/steering/product.md")]
    [InlineData(".spec-workflow/steering/roles.md")]
    public void EveryProjectAnAgentFileNames_IsInTheSolution(string file) => AssertProjectNames(file);

    private static void AssertProjectNames(string document)
    {
        var known = SolutionProjectNames();

        // "EtAlii.Adp" alone is the namespace root as well as a project, and a module name like
        // EtAlii.Adp.Diagram.Timeline is a project too - both are in the solution, so no special
        // case is needed. What IS excluded is a name used as a namespace prefix in prose.
        var unknown = ProjectNamesIn(document)
            .Where(name => !known.Contains(name))
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            $"{document} names {unknown.Length} EtAlii.Adp.* project(s) that are not in EtAlii.Adp.slnx. A project "
            + "was renamed, removed, or never existed - the list in this document must follow the solution:"
            + Environment.NewLine + string.Join(Environment.NewLine, unknown));
    }

    [Theory]
    [InlineData(ArchitecturePage)]
    [InlineData(StructurePage)]
    public void APageStaysWithinItsSize(string page)
    {
        var text = Read(page);
        var lines = text.Split('\n').Length;
        var diagrams = Regex.Matches(text, "^```mermaid", RegexOptions.Multiline).Count;

        Assert.True(lines <= LineLimit, $"{page} is {lines} lines, over the limit of {LineLimit}. Link another document rather than growing this one.");
        Assert.True(diagrams <= DiagramLimit, $"{page} has {diagrams} mermaid diagrams, over the limit of {DiagramLimit}.");
    }

    [Theory]
    [InlineData(ArchitecturePage)]
    [InlineData(StructurePage)]
    public void APageStillNamesAsManyPathsAsItDid(string page)
    {
        var found = PathsIn(page).Length;

        Assert.True(
            found >= PathFloor,
            $"{page} names {found} repo-relative paths, below the floor of {PathFloor}. If the page legitimately "
            + "names fewer, lower the floor here and say why. If not, the page has been emptied or reshaped so this "
            + "test's pattern no longer matches it - which would let it pass while guarding nothing.");
    }

    [Fact]
    public void TheStructurePageStatesAsManyCountsAsItDid()
    {
        var found = CountExpression().Matches(Read(StructurePage)).Count;

        Assert.True(
            found >= CountFloor,
            $"{StructurePage} states {found} counts in the **n** form, below the floor of {CountFloor}. If the page "
            + "legitimately states fewer, lower the floor here and say why. If not, the counts have been removed or "
            + "reworded out of the form this test reads - which would let it pass while checking nothing.");
    }

    [Fact]
    public void EveryCountTheStructurePageStates_Recomputes()
    {
        var text = Read(StructurePage);
        var expected = ExpectedCounts();
        var wrong = new List<string>();

        foreach ((string label, int value) in expected)
        {
            // The page states each count as **n** somewhere; this asserts the VALUE is present in
            // that form rather than trying to bind a label to a position in prose, which would make
            // the guard a parser of English.
            var stated = CountExpression().Matches(text).Select(match => int.Parse(match.Groups[1].Value)).ToHashSet();
            if (!stated.Contains(value))
            {
                wrong.Add($"{label}: the tree says {value}, which appears nowhere on the page as **{value}**");
            }
        }

        Assert.True(
            wrong.Count == 0,
            $"{StructurePage} states counts that no longer match the tree. Recompute and update the page - the tree "
            + "is right and the page is stale:"
            + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    [Theory]
    [InlineData(ArchitecturePage)]
    [InlineData(StructurePage)]
    public void EveryCountAPageStates_IsOneTheTreeRecomputes(string page)
    {
        // The other direction. The test above asks whether every recomputed value is still on the
        // page; this asks whether every **n** on the page is still a recomputed value. Without it a
        // stale figure survives as long as the fresh one also appears somewhere, and a new count
        // added to a page is never checked at all - Requirement 5.2 says EVERY stated count.
        var recomputed = ExpectedCounts().Values.ToHashSet();
        var stray = CountExpression().Matches(Read(page))
            .Select(match => int.Parse(match.Groups[1].Value))
            .Where(value => !recomputed.Contains(value))
            .Distinct()
            .ToArray();

        Assert.True(
            stray.Length == 0,
            $"{page} states {string.Join(", ", stray.Select(value => $"**{value}**"))}, which this guard does not "
            + "recompute. Either the figure is stale, or it is a new count: add its recomputation to ExpectedCounts, or, "
            + "if the tree cannot give it, remove it from the page (Requirement 3.3).");
    }
}
