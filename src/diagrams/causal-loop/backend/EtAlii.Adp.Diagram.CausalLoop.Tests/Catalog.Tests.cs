using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// This module's row in <c>docs/diagrams.md</c> (causal-loop-diagram Requirement 12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this module keeps its own catalog test.</b> The shared coherence check in
/// <c>DiagramDiscoveryStartup.Tests</c> selects definitions with <c>Build is null</c> — the ones
/// that register no services of their own — because a definition that does register services may
/// legitimately annotate its row beyond the plain title. That scoping is deliberate and correct,
/// and it means causal-loop, which carries a <c>Build</c>, is outside it.
/// </para>
/// <para>
/// Verified rather than assumed: deleting the row and re-running the shared check left it green.
/// So the repository's catalog rule is unguarded for every module in this position unless the
/// module guards it, which is what this does.
/// </para>
/// </remarks>
public class CatalogTests
{
    private static string CatalogPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(IoPath.Combine(directory.FullName, "docs", "diagrams.md")))
            {
                directory = directory.Parent;
            }

            Assert.SkipWhen(directory is null, "The repository's docs/diagrams.md was not found from the test output.");
            return IoPath.Combine(directory.FullName, "docs", "diagrams.md");
        }
    }

    private static string Row()
    {
        var row = File.ReadLines(CatalogPath)
            .FirstOrDefault(line => line.Contains("systems/causal-loop-diagram", StringComparison.Ordinal));

        Assert.NotNull(row);
        return row;
    }

    /// <summary>
    /// Requirement 12.2: the origin tag exactly, because an <c>.adp</c> names it on its first
    /// line and a later change would have to reach every document already carrying it.
    /// </summary>
    [Fact]
    public void TheCatalog_CarriesThisModulesOriginExactly()
    {
        // Act & assert.
        Assert.Contains("<code>systems/causal-loop-diagram</code>", Row(), StringComparison.Ordinal);

        // The same string the module registers, so the two cannot drift apart.
        Assert.Equal("systems/causal-loop-diagram", ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin.Key);
    }

    /// <summary>
    /// Requirement 12.2 again: the state icon must match the real state, and the legend defines
    /// the vocabulary. Any of the legend's icons is acceptable here — this test's job is that the
    /// row carries one at all, because a row with no state is the one failure mode a reader
    /// cannot detect by looking.
    /// </summary>
    [Fact]
    public void TheRow_CarriesAStateFromTheLegend()
    {
        // Arrange.
        string[] states = ["Identified", "Specified", "To-do", "Work-in-progress", "Prototype", "Implemented"];

        // Act & assert.
        var row = Row();
        Assert.Single(states, state => row.Contains($"&nbsp;{state}<", StringComparison.Ordinal));
    }

    /// <summary>
    /// The row names the specification, which is where a reader goes to find out why the
    /// notation was adopted the way it was.
    /// </summary>
    /// <remarks>
    /// This asserted a link to <c>causal-loop-diagram/requirements.md</c> until 2026-09-11, when
    /// the archived specification was removed from the tree by the user's ruling. The row now
    /// names it and says where its text is: in history, before the commit that recorded the
    /// removal. What the reader needs is the name, and whether a link resolves is
    /// <c>DocumentationLinksTests</c>' question, not this one's - asserting a path here made
    /// this a second copy of the catalog's text.
    /// </remarks>
    [Fact]
    public void TheRow_NamesTheSpecification()
    {
        // Act & assert. The bare name, not the origin tag <c>systems/causal-loop-diagram</c>,
        // which contains it and would satisfy a looser match on its own.
        Assert.Contains("<code>causal-loop-diagram</code>", Row(), StringComparison.Ordinal);
    }
}
