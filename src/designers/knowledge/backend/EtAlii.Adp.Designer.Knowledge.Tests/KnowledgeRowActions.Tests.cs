using EtAlii.Adp.Context;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;
using ContextScope = EtAlii.Adp.Documents.Wire.ContextScope;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// "Linked from (N)" on a selected row of a knowledge table (knowledge.md; FBL 17.5, etalii.adp spec 092): offered where links of
/// the workspace point at the row, it lists them by the element that holds each and its document, and opens the one picked.
/// Found missing by the spec 093 host test, which also met the ribbon holding a timeline's actions for a selected row.
/// </summary>
public sealed class KnowledgeRowActionsTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-knowledge-row-actions");
    private readonly string _root;
    private readonly string _cities;
    private readonly string _plan;
    private readonly KnowledgeRowActionProvider _provider;

    public KnowledgeRowActionsTests()
    {
        _root = _testFolder.Path;
        _cities = IoPath.Combine(_root, "cities.yaml");
        _plan = IoPath.Combine(_root, "plan.tml");
        var workspace = new LinkWorkspace(_root.Replace('\\', '/'), [
            new IndexedDocument(_cities.Replace('\\', '/'), "c4t7y2ma", "name", [Element("r1", "Amsterdam"), Element("r2", "Antwerp")], []) { Origin = "etalii/knowledge" },
            new IndexedDocument(_plan.Replace('\\', '/'), "t7m2k9qa", "label", [new IndexedElement("m1", [new KeyValuePair<string, string>("label", "Visit")])], [new HeldLink("m1", "$c4t7y2ma@cities.yaml#r2=Antwerp") { Element = "m1" }]) { Origin = "generic/timeline" },
        ]);
        _provider = new KnowledgeRowActionProvider(new FixedLinkIndex(workspace));
    }

    public void Dispose() => _testFolder.Dispose();

    private static IndexedElement Element(string id, string name) => new(id, [new KeyValuePair<string, string>("name", name)]);

    private ContextTarget Row(string rowId) =>
        new(ContextScope.DiagramElement, _cities, IsContainer: false, SourceId: default, ProjectRoot.From(_root), default, rowId, KnowledgeRowSourceResolver.RowOrigin);

    [Fact]
    public async Task ARowLinksPointAt_OffersLinkedFrom_ListsThem_AndOpensThePickedOne()
    {
        // Act.
        var offered = await _provider.DiscoverAsync(Row("r2"), TestContext.Current.CancellationToken);
        var listed = await _provider.ExecuteAsync(Row("r2"), KnowledgeRowActionProvider.LinkedFrom, TestContext.Current.CancellationToken);
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(listed).Request;
        var picked = await _provider.CommitAsync(Row("r2"), KnowledgeRowActionProvider.LinkedFrom, Assert.Single(choice.Options).Id, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Linked from (1)…", Assert.Single(Assert.Single(offered).Actions).Label);
        Assert.Equal("Visit (plan.tml)", Assert.Single(choice.Options).Label);
        Assert.True(picked.Completed, picked.Error);
        var reveal = Assert.IsType<ContextExecutionReveals>(picked.Next);
        Assert.Equal((IoPath.GetFullPath(_plan), "m1"), (reveal.FullPath, reveal.ElementId));
    }

    [Fact]
    public async Task ARowNoLinkPointsAt_OffersNothing_AndNeitherDoesAnotherToolsElement()
    {
        // Act.
        var unlinked = await _provider.DiscoverAsync(Row("r1"), TestContext.Current.CancellationToken);
        var other = await _provider.DiscoverAsync(Row("r2") with { Origin = new ToolOrigin("generic", "timeline") }, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(unlinked);
        Assert.Empty(other);
    }
}
