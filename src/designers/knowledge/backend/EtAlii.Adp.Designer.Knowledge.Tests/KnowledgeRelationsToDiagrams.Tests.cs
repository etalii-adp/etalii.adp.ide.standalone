using System.Text;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// A knowledge relation whose target is a diagram (spec 092, FR-025): the diagram is offered as a target, a cell links to its
/// elements by their name, and "Create" adds an element to the diagram through its tool type, in one undo step over both files.
/// </summary>
/// <remarks>
/// The diagram is a timeline the workspace's index holds, <c>plan.tml</c>, with one moment, <c>launch</c>, named Launch. What its
/// tool type does to its bytes is stood in for by a line appended per element, which is all a knowledge file needs of it.
/// </remarks>
public sealed class KnowledgeRelationsToDiagramsTests : IDisposable
{
    private const string PlanId = "t7m2k9qa";
    private const string PlanText = "timeline: 1\nid: t7m2k9qa\nelements:\n  - id: launch\n    label: Launch\n";

    private readonly TestFolder _testFolder = TestFolder.Create("adp-knowledge-diagram-relations");
    private readonly string _root;

    public KnowledgeRelationsToDiagramsTests()
    {
        _root = _testFolder.Path;
        File.WriteAllText(Plan, PlanText);
    }

    public void Dispose() => _testFolder.Dispose();

    private string Plan => IoPath.Combine(_root, "plan.tml");

    private FixedLinkIndex Links(params LinkElementKind[] kinds)
    {
        var workspace = new LinkWorkspace(_root.Replace('\\', '/'), [
            new IndexedDocument(Plan.Replace('\\', '/'), PlanId, "label", [new IndexedElement("launch", [new KeyValuePair<string, string>("label", "Launch")])], []) { Origin = "generic/timeline" },
        ]);
        return new FixedLinkIndex(workspace, kinds, (bytes, kind, id, name) => [.. bytes, .. Encoding.UTF8.GetBytes($"  - id: {id}\n    kind: {kind}\n    label: {name}\n")]);
    }

    private async Task<(EditingTable Table, string RelationId)> RelatedToPlanAsync(FixedLinkIndex links)
    {
        var table = new EditingTable(KnowledgeFiles.CopyPlain("cities.yaml", _root), rootPath: _root, links: links);
        await table.Edit(new TableGesture("addRelation", Values: ["Milestones"], Settings: Settings(("target", "plan.tml"), ("limit", "none"), ("twoWay", "true"))));
        return (table, table.OnDisk().Properties.Single(property => property.Name == "Milestones").Id);
    }

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    private static TableCell? Cell(IEnumerable<TableRow> lines, string rowId, string columnId) => lines.Single(line => line.Id == rowId).Cells.FirstOrDefault(cell => cell.ColumnId == columnId);

    [Fact]
    public async Task ADiagram_IsOfferedAsARelationsTarget()
    {
        // Arrange.
        await using var table = new EditingTable(KnowledgeFiles.CopyPlain("cities.yaml", _root), rootPath: _root, links: Links());

        // Act: a relation asked for without a target.
        (_, string asked) = table.Begin(new TableGesture("addRelation", Values: ["Milestones"]));

        // Assert: the timeline is among the tables to choose from, by its file name.
        Assert.Contains("\"id\":\"plan.tml\",\"label\":\"plan.tml\"", asked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARelationToADiagram_NamesItByALink_IsOneWay_AndLinksACellToAnElementByItsName()
    {
        // Arrange.
        (EditingTable table, string relation) = await RelatedToPlanAsync(Links());
        await using var owned = table;

        // Act.
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: relation, Values: ["launch"]));

        // Assert: the target is the timeline's document, there is no backlink name a diagram could not show, and the cell is a link to the moment.
        var property = table.OnDisk().Properties.Single(candidate => candidate.Id == relation);
        Assert.Equal($"${PlanId}@plan.tml", property.TargetFile);
        Assert.Equal("", property.BacklinkName);
        Assert.Contains($"${PlanId}@plan.tml#launch.label=Launch", Encoding.UTF8.GetString(table.Bytes()), StringComparison.Ordinal);
        Assert.Equal(["Launch"], Cell(table.Lines(), "r1", relation)?.Labels);
    }

    [Fact]
    public async Task CreateFromTheCell_AddsTheElementToTheDiagram_AndLinksIt_AndOneUndoTakesBothBack()
    {
        // Arrange.
        (EditingTable table, string relation) = await RelatedToPlanAsync(Links(new LinkElementKind("Moment", "moment")));
        await using var owned = table;
        var cities = table.Bytes();

        // Act: "Retro" typed and created; the diagram has one kind of element, so none is named.
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: relation, Values: [], Settings: Settings(("newRow", "Retro"))));

        // Assert: the diagram has the element, by the id its tool type made, and the city links to it.
        Assert.Equal(PlanText + "  - id: new00001\n    kind: Moment\n    label: Retro\n", await File.ReadAllTextAsync(Plan, TestContext.Current.CancellationToken));
        Assert.Contains($"${PlanId}@plan.tml#new00001.label=Retro", Encoding.UTF8.GetString(table.Bytes()), StringComparison.Ordinal);

        // Act: one undo.
        await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: both files are as they were.
        Assert.Equal(cities, table.Bytes());
        Assert.Equal(PlanText, await File.ReadAllTextAsync(Plan, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithSeveralKinds_CreateMakesTheOneAskedFor_AndAsksWhenNoneIs()
    {
        // Arrange.
        (EditingTable table, string relation) = await RelatedToPlanAsync(Links(new LinkElementKind("Moment", "moment"), new LinkElementKind("Period", "period")));
        await using var owned = table;
        var cities = table.Bytes();

        // Act: created without a kind, then as a period.
        (var _, string unasked) = table.Begin(new TableGesture("setCell", RowId: "r1", ColumnId: relation, Values: [], Settings: Settings(("newRow", "Sprint"))));
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: relation, Values: [], Settings: Settings(("newRow", "Sprint"), ("newKind", "Period"))));

        // Assert: the first is refused and writes nothing; the second makes a period.
        Assert.Equal(KnowledgeCellEdits.ChooseAKind, unasked);
        Assert.NotEqual(cities, table.Bytes());
        Assert.Contains("kind: Period\n    label: Sprint", await File.ReadAllTextAsync(Plan, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiagramThatCanBeGivenNoElement_RefusesCreate_AndWritesNothing()
    {
        // Arrange: the index offers no kind of element for the timeline.
        (EditingTable table, string relation) = await RelatedToPlanAsync(Links());
        await using var owned = table;
        var cities = table.Bytes();

        // Act.
        (var _, string answer) = table.Begin(new TableGesture("setCell", RowId: "r1", ColumnId: relation, Values: [], Settings: Settings(("newRow", "Retro"))));

        // Assert.
        Assert.Equal(KnowledgeCellEdits.CannotCreateInDiagram, answer);
        Assert.Equal(cities, table.Bytes());
        Assert.Equal(PlanText, await File.ReadAllTextAsync(Plan, TestContext.Current.CancellationToken));
    }
}
