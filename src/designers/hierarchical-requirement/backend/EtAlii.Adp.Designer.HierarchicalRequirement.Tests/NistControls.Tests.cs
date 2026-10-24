using System.Diagnostics;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The large shipped example, the controls of NIST SP 800-53 Revision 5 written from NIST's OSCAL catalog: it reads as its
/// readme says, without a finding, and opens, shows another program's change and writes one edit on the page as the web shop
/// does, each within four seconds at more than four times the size SC-003 sets its two seconds for.
/// </summary>
public sealed class NistControlsTests : IDisposable
{
    /// <summary>
    /// How long opening and showing a change may take at 2,616 entries. SC-003's two seconds are set for 600 and held at that
    /// size by <see cref="LargeDocumentTests"/>; here they left a shared CI runner no room (2,011 ms on 2026-10-24), so this
    /// guard holds twice that, which still fails a reading that grows faster than the document.
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(4);

    private static string ExampleFolder => RepositoryPaths.Under("src", "examples", "designers", "hierarchical-requirement", "nist-sp-800-53");

    private readonly string _root = Directory.CreateTempSubdirectory("adp-requirement-nist-").FullName;

    private string Body => IoPath.Combine(_root, "nist-sp-800-53.md");

    public NistControlsTests()
    {
        File.Copy(IoPath.Combine(ExampleFolder, "nist-sp-800-53.md"), Body);
        File.Copy(IoPath.Combine(ExampleFolder, "nist-sp-800-53.adp"), IoPath.ChangeExtension(Body, ".adp"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }

    private static HierarchicalRequirementDocument Read() => HierarchicalRequirementBody.Read(
        File.ReadAllBytes(IoPath.Combine(ExampleFolder, "nist-sp-800-53.md")), "nist-sp-800-53.md",
        File.ReadAllBytes(IoPath.Combine(ExampleFolder, "nist-sp-800-53.adp")), "nist-sp-800-53.adp").Document;

    [Fact]
    public void TheControls_ReadWithoutAFinding_AsTheirReadmeSays()
    {
        // Act.
        var document = Read();

        // Assert: every number given once, every reference to a related control resolved, every requirement in an item.
        Assert.Empty(document.Findings);

        // Twenty families, 300 controls and 714 enhancements, three levels deep, and 1,582 requirements.
        var byId = document.Entries.ToDictionary(entry => entry.Id);
        Assert.Equal(1034, document.Entries.Count(entry => entry is Item));
        Assert.Equal(1582, document.Entries.Count(entry => entry is Requirement));
        Assert.Equal(20, document.Entries.Count(entry => entry.ParentId is null));
        Assert.Equal(3, document.Entries.Where(entry => entry is Item).Max(entry => Depth(byId, entry)));
        Assert.Equal("AC-2(1) Automated System Account Management", byId["ITEM-0004"].Title);

        // The four baselines as tags, the families collapsed, three stored filters.
        Assert.Equal(["low", "moderate", "high", "privacy"], document.Tags.Select(tag => tag.Name));
        Assert.Equal(20, document.Entries.Count(entry => entry.Collapsed));
        Assert.Equal(["High baseline", "Privacy baseline", "Access control"], document.Filters.Select(filter => filter.Name));
    }

    [Fact]
    public async Task TheControls_OpenOnThePage_AndShowAnOutsideChange_EachWithinFourSeconds()
    {
        // Act: opened.
        var opening = Stopwatch.StartNew();
        await using var page = RequirementPage.Open(Body);
        var opened = page.Session.Model();
        opening.Stop();

        // Assert.
        Assert.Equal(1582, opened.Nodes.Count(node => node.Role == HierarchySurfaceRoles.Entry));
        Assert.True(opening.Elapsed < Limit, $"Opening took {opening.Elapsed.TotalMilliseconds:F0} ms.");

        // Act: another program changes the last requirement's title.
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        var showing = Stopwatch.StartNew();
        await File.WriteAllTextAsync(Body, text.Replace("REQ-1582 ", "REQ-1582 Changed by an agent: ", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var shown = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Id == "REQ-1582" && node.Title.StartsWith("Changed by an agent: ", StringComparison.Ordinal)), Limit);
        showing.Stop();

        // Assert.
        Assert.NotNull(shown);
        Assert.True(showing.Elapsed < Limit, $"Showing the change took {showing.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task AnEditOfOneTitle_WritesOnlyThatLine()
    {
        // Arrange.
        var before = await File.ReadAllLinesAsync(Body, TestContext.Current.CancellationToken);
        await using var page = RequirementPage.Open(Body);

        // Act.
        (ShortGuid editId, string refusal) = page.Gesture(TextOperations.SetTitle, "ITEM-0004", ("title", "AC-2(1) Automated account management"));
        var outcome = await page.OutcomeAsync(editId);

        // Assert: written, and every other line of the 2,616 entries as it was.
        Assert.Equal("", refusal);
        Assert.NotNull(outcome);
        Assert.True(outcome.Written, outcome.Error);
        var after = await File.ReadAllLinesAsync(Body, TestContext.Current.CancellationToken);
        Assert.Equal(before.Length, after.Length);
        var changed = Assert.Single(Enumerable.Range(0, before.Length), index => before[index] != after[index]);
        Assert.Equal("    - ITEM-0004 AC-2(1) Automated account management", after[changed]);
    }

    /// <summary>How many items stand above and at <paramref name="entry"/>, counting itself.</summary>
    private static int Depth(Dictionary<string, RequirementEntry> byId, RequirementEntry entry)
    {
        var depth = 0;
        for (var at = entry; at is not null; at = at.ParentId is { } parent ? byId[parent] : null) depth++;
        return depth;
    }
}
