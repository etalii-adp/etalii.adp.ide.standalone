using System.Text.RegularExpressions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// Every file <c>docs/guards.md</c> names still exists, so a renamed or deleted guard breaks that
/// document rather than quietly outliving it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The document exists because a guard nobody can find only speaks after the mistake.</b> On
/// 2026-09-22 three guards each caught something nobody was looking for; the set was discoverable
/// only by asking somebody who happened to know. This test is the cheap half of keeping that page
/// true: it cannot check that a DESCRIPTION is still accurate - no test can - so it checks the half
/// that rots silently, which is the paths.
/// </para>
/// <para>
/// <b>Modelled on <see cref="DocumentationLinksTests"/></b>, which does the same job for links in the
/// delivered documentation, and for the same reason: a page whose references have gone stale fails
/// the build rather than a reader.
/// </para>
/// <para>
/// <b>The floor is the canary.</b> A guard that reads a document and asserts "every path I found
/// exists" passes vacuously when the document is emptied, reorganised into a form the pattern no
/// longer matches, or moved - the exact shape this repository has been bitten by (a rule that knew
/// one spelling of a file write, a filter that could not see a warning without a rule id). So the
/// count is asserted too, and the assertion names what to do if the shape legitimately changed.
/// </para>
/// </remarks>
public partial class GuardInventoryTests
{
    private const string Inventory = "docs/guards.md";

    /// <summary>
    /// A floor deliberately BELOW the count - 35 paths when this was written - so that adding or
    /// removing a row does not have to touch this number, while an emptied document, a reshaped
    /// one this pattern no longer matches, or a wholesale deletion does. An exact count would be a
    /// second copy of the document, needing an edit every time its subject legitimately changed.
    /// </summary>
    private const int KnownFloor = 25;

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

    /// <summary>Backticked paths that look like a file in this repository: a slash, and an extension we guard in.</summary>
    [GeneratedRegex(@"`([A-Za-z0-9_./\- ]+\.(?:cs|ts|tsx|sh|md|props|editorconfig))`")]
    private static partial Regex NamedFileExpression();

    private static string[] NamedFiles()
    {
        var text = File.ReadAllText(IoPath.Combine(RepositoryRoot, Inventory));
        return NamedFileExpression()
            .Matches(text)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(path => path.Contains('/', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void EveryGuardTheInventoryNames_Exists()
    {
        var missing = NamedFiles()
            .Where(path => !File.Exists(IoPath.Combine(RepositoryRoot, path.Replace('/', IoPath.DirectorySeparatorChar))))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{Inventory} names {missing.Length} file(s) that do not exist. Either the guard moved and its row needs "
            + "updating, or it was deleted and its row must go - with a note saying what now answers its question:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void TheInventory_StillNamesAsManyGuardsAsItDid()
    {
        var found = NamedFiles().Length;

        Assert.True(
            found >= KnownFloor,
            $"{Inventory} names {found} file paths, below the floor of {KnownFloor}. If guards were legitimately "
            + "removed, lower the floor in this test and say why in the commit. If not, the document has been "
            + "emptied or reshaped so this test's pattern no longer matches it - which would let it pass while "
            + "guarding nothing.");
    }
}
