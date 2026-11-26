using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// Relations between knowledge files (knowledge-designer Requirement 5): a relation names its
/// target by a path and its values by the target's row ids, shows them by their titles, and keeps
/// them when the target or a row is not there; a two-way relation is made in both files in one
/// step and shown from both sides; and the parent relation never makes a row its own ancestor.
/// </summary>
/// <remarks>
/// The shipped example is the subject: <c>cities</c> relates to <c>provinces.yaml</c> through its
/// property <c>p5</c>, which holds one row, and its first row names the province <c>nh</c>.
/// </remarks>
public sealed class KnowledgeRelationsTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-knowledge-relations");
    private readonly string _root;

    public KnowledgeRelationsTests()
    {
        _root = _testFolder.Path;
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private string Provinces => IoPath.Combine(_root, KnowledgeFiles.Related);

    private EditingTable Open(string extension = ".yaml", KnowledgeDocuments? documents = null) => new(KnowledgeFiles.CopyPlain("cities" + extension, _root), documents: documents);

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    private static TableCell? Cell(IEnumerable<TableRow> lines, string rowId, string columnId) => lines.Single(line => line.Id == rowId).Cells.FirstOrDefault(cell => cell.ColumnId == columnId);

    private static KnowledgeProperty Property(EditingTable table, string name) => table.OnDisk().Properties.Single(property => property.Name == name);

    /// <summary>
    /// The cities related to the provinces by a two-way relation as a file made before links holds it: <c>Near</c> (<c>nr</c>)
    /// here, naming its counterpart <c>Cities nearby</c> (<c>cn</c>) there, which is computed and names it back.
    /// </summary>
    private EditingTable OpenMadeBeforeLinks(KnowledgeDocuments? documents = null)
    {
        var path = KnowledgeFiles.CopyPlainMadeBeforeLinks("cities.yaml", _root);
        File.WriteAllText(path, File.ReadAllText(path).Replace("  - id: p6\r\n", "  - id: nr\r\n    name: Near\r\n    type: relation\r\n    target: provinces.yaml\r\n    counterpart: cn\r\n  - id: p6\r\n", StringComparison.Ordinal));
        File.WriteAllText(Provinces, File.ReadAllText(Provinces).Replace("views:\r\n", "  - id: cn\r\n    name: Cities nearby\r\n    type: relation\r\n    target: cities.yaml\r\n    counterpart: nr\r\n    computed: true\r\nviews:\r\n", StringComparison.Ordinal));
        return new EditingTable(path, documents: documents);
    }

    // ---- what a relation shows ----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARelatedRow_IsStoredByItsId_AndShownByItsTitle(string extension)
    {
        // Arrange.
        await using var table = Open(extension);

        // Act.
        var baseline = table.Session.Baseline();
        var cell = Cell(table.Lines(), "r1", "p5");

        // Assert: the id in the file, the title on the screen, and the target's rows to choose from.
        Assert.Equal(["nh"], cell?.Values);
        Assert.Equal(["Noord-Holland"], cell?.Labels);
        var column = baseline.Columns.Single(candidate => candidate.Id == "p5");
        Assert.Equal(["nh:Noord-Holland", "zh:Zuid-Holland", "an:Antwerpen", "9wvrdvjn4ob2f2nw421bouq7y:Limburg", "cdns3i0ehcr45q7zh0z15iqzt:Overijssel"], column.Options.Select(option => $"{option.Id}:{option.Name}"));
        Assert.False(column.Settings.ContainsKey("unresolved"));
        Assert.Empty(baseline.Findings);
    }

    [Fact]
    public async Task ARelationWhoseTargetIsNotThere_KeepsItsValues_AndSaysSo()
    {
        // Arrange: the table the relation names is gone.
        var path = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        File.Delete(Provinces);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using var table = new EditingTable(path);

        // Act.
        var baseline = table.Session.Baseline();
        var cell = Cell(table.Lines(), "r1", "p5");

        // Assert: reported on the property, the value still there and shown as what it is, the file untouched.
        var finding = Assert.Single(baseline.Findings);
        Assert.Equal((KnowledgeValidator.UnresolvedTarget, TableFindingSeverity.Warning, "p5"), (finding.Code, finding.Severity, finding.ColumnId));
        Assert.Equal(["nh"], cell?.Values);
        Assert.Equal([""], cell?.Labels);
        Assert.Equal("true", baseline.Columns.Single(column => column.Id == "p5").Settings["unresolved"]);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AValueNamingARowThatIsNotInTheTarget_IsKept_AndReportedOnItsCell()
    {
        // Arrange: the first city names a province nobody has.
        var path = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, text.Replace("- row: $p7v3k9qd@provinces.yaml#nh.Name=Noord-Holland", "- row: $p7v3k9qd@provinces.yaml#atlantis", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await using var table = new EditingTable(path);

        // Act.
        var finding = Assert.Single(table.Session.Baseline().Findings);

        // Assert.
        Assert.Equal((KnowledgeValidator.UnresolvedRow, "r1", "p5"), (finding.Code, finding.RowId, finding.ColumnId));
        Assert.Equal(["atlantis"], Cell(table.Lines(), "r1", "p5")?.Values);
    }

    [Fact]
    public async Task ARelatedRow_IsARowOfTheTarget_AndNoMoreThanTheRelationHolds()
    {
        // Arrange.
        await using var table = Open();
        var before = table.Bytes();

        // Act.
        (_, string stranger) = table.Begin(new TableGesture("setCell", RowId: "r2", ColumnId: "p5", Values: ["atlantis"]));
        (_, string several) = table.Begin(new TableGesture("setCell", RowId: "r2", ColumnId: "p5", Values: ["nh", "zh"]));

        // Assert.
        Assert.Equal("That row is not in Provinces.", stranger);
        Assert.Equal("This relation holds one row.", several);
        Assert.Equal(before, table.Bytes());

        // Act.
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p5", Values: ["an"]));

        // Assert.
        Assert.Equal(["an"], table.OnDisk().Rows.Single(row => row.Id == "r2").Cells.Single(cell => cell.PropertyId == "p5").Values);
        Assert.Equal(["Antwerpen"], Cell(table.Lines(), "r2", "p5")?.Labels);
    }

    [Fact]
    public async Task ARenamedRowOverThere_IsShownUnderItsNewNameHere_AtOnce()
    {
        // Arrange: both tables open, as two tabs of one application are.
        var documents = new KnowledgeDocuments();
        await using var cities = Open(documents: documents);
        await using var provinces = new EditingTable(Provinces, documents: documents);
        Assert.Equal(["Noord-Holland"], Cell(cities.Lines(), "r1", "p5")?.Labels);

        // Act: the province is renamed in its own table.
        await provinces.Edit(new TableGesture("setCell", RowId: "nh", ColumnId: "n1", Values: ["North Holland"]));

        // Assert: nothing in the cities' file names the province by its name, so nothing there changed - and it shows the new one.
        Assert.Equal(["North Holland"], Cell(cities.Lines(), "r1", "p5")?.Labels);
        Assert.Equal(["nh"], cities.OnDisk().Rows.Single(row => row.Id == "r1").Cells.Single(cell => cell.PropertyId == "p5").Values);
    }

    // ---- making a relation ----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task AOneWayRelation_IsAPropertyOfThisFile_AndTheTargetIsNotTouched(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        var target = await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken);

        // Act.
        await table.Edit(new TableGesture("addRelation", Values: ["Twinned with"], Settings: Settings(("target", KnowledgeFiles.Related), ("limit", "none"))));

        // Assert: the target by its path from this file, any number of rows, and no other side.
        var relation = Property(table, "Twinned with");
        Assert.Equal(("relation", KnowledgeFiles.RelatedLink, "none", "", false), (relation.ValueType, relation.TargetFile, relation.Limit, relation.Counterpart, relation.IsComputed));
        Assert.Equal(target, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Empty(table.Session.Baseline().Findings);
    }

    [Fact]
    public async Task ARelationToATableWithADocumentId_NamesItByALink_AndItsRowsByLinksTitledFromThere()
    {
        // Arrange: the provinces hold a document id.
        await using var table = Open();
        await File.WriteAllTextAsync(Provinces, (await File.ReadAllTextAsync(Provinces, TestContext.Current.CancellationToken)).Replace("designer: etalii/knowledge\r\n", "designer: etalii/knowledge\r\nid: k3f9x2ma\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var target = await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken);

        // Act: a relation to them, and a row of theirs in a cell.
        await table.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("limit", "none"))));
        var near = Property(table, "Near");
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near.Id, Values: ["nh"]));

        // Assert: the target by a link to its document, the row by a link named by the title property and the row's title, shown by that title.
        Assert.Equal("$k3f9x2ma@provinces.yaml", near.TargetFile);
        Assert.Contains("$k3f9x2ma@provinces.yaml#nh.Name=Noord-Holland", System.Text.Encoding.UTF8.GetString(table.Bytes()), StringComparison.Ordinal);
        Assert.Equal(["nh"], table.OnDisk().Rows.Single(row => row.Id == "r1").Cells.Single(cell => cell.PropertyId == near.Id).Values);
        Assert.Equal(["Noord-Holland"], Cell(table.Lines(), "r1", near.Id)?.Labels);
        Assert.Equal(target, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Empty(table.Session.Baseline().Findings);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ATwoWayRelation_IsMadeInThisFileAlone_UnderItsBacklinkName_AndUndoneInOne(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        await table.Edit(new TableGesture("addRelation", Values: ["Seat of"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Seats"))));

        // Assert: the relation names the column its target shows its backlinks in; the target is not written (knowledge.md, section 9).
        var here = Property(table, "Seat of");
        Assert.Equal(("Seats", "", false), (here.BacklinkName, here.Counterpart, here.IsComputed));
        Assert.Equal("Seats", table.ColumnSettings(here.Id)["backlinkName"]);
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: one undo.
        var undone = await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: as it was.
        Assert.True(undone.IsSuccess);
        Assert.Equal(ours, table.Bytes());

        // Act: and one redo.
        var redone = await table.History.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redone.IsSuccess);
        Assert.Equal("Seats", Property(table, "Seat of").BacklinkName);
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheBacklinkColumn_ShowsTheRowsThatLinkHere_AndIsInNoFile_AndIsNotFilledInFromThere()
    {
        // Arrange: a two-way relation, and both tables open; the provinces know the project the cities are in.
        var documents = new KnowledgeDocuments();
        await using var cities = Open(documents: documents);
        await cities.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(cities, "Near").Id;
        await using var provinces = new EditingTable(Provinces, documents: documents, rootPath: _root);
        var theirs = provinces.Bytes();
        var nearby = provinces.Session.Baseline().Columns.Single(column => column.Name == "Cities nearby");

        // Act: values are given on the side that holds them.
        await cities.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near, Values: ["nh", "zh"]));
        await cities.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: near, Values: ["zh"]));

        // Assert: the backlink column shows them at once, by title, and is in neither file.
        var lines = provinces.Lines();
        Assert.Equal(["r1"], Cell(lines, "nh", nearby.Id)?.Values);
        Assert.Equal(["r1", "r2"], Cell(lines, "zh", nearby.Id)?.Values);
        Assert.Equal(["Amsterdam", "Antwerp"], Cell(lines, "zh", nearby.Id)?.Labels);
        Assert.Null(Cell(lines, "an", nearby.Id));
        Assert.Equal("true", nearby.Settings["computed"]);
        Assert.Equal(theirs, provinces.Bytes());
        Assert.DoesNotContain(cities.OnDisk().Properties, property => property.Name == "Cities nearby");

        // Act: it is not filled in from here.
        (_, string answer) = provinces.Begin(new TableGesture("setCell", RowId: "an", ColumnId: nearby.Id, Values: ["r2"]));

        // Assert.
        Assert.Equal("This side of the relation is filled in from the other table.", answer);
        Assert.Equal(theirs, provinces.Bytes());
    }

    [Fact]
    public async Task DeletingATwoWayRelation_TakesItsBacklinkColumnAlong_AndWritesNoOtherFile()
    {
        // Arrange: a two-way relation, shown in the open provinces as its backlink column.
        var documents = new KnowledgeDocuments();
        await using var cities = Open(documents: documents);
        await cities.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(cities, "Near").Id;
        await using var provinces = new EditingTable(Provinces, documents: documents, rootPath: _root);
        var theirs = provinces.Bytes();
        Assert.Contains(provinces.Session.Baseline().Columns, column => column.Name == "Cities nearby");

        // Act: deleted, with no question to answer first.
        await cities.Edit(new TableGesture("deleteColumn", ColumnId: near));

        // Assert: the column went with it; the provinces' file was never written.
        Assert.DoesNotContain(cities.OnDisk().Properties, property => property.Id == near);
        Assert.DoesNotContain(provinces.Session.Baseline().Columns, column => column.Name == "Cities nearby");
        Assert.Equal(theirs, provinces.Bytes());

        // Act: one undo.
        Assert.True((await cities.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);

        // Assert: and it is back.
        Assert.Contains(provinces.Session.Baseline().Columns, column => column.Name == "Cities nearby");
    }

    [Fact]
    public async Task DeletingOneSideOfATwoWayRelationMadeBeforeLinks_AsksWhatBecomesOfTheOtherSide()
    {
        // Arrange: a two-way relation between the two files, as a file made before links holds it.
        await using var table = OpenMadeBeforeLinks();
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: deleted without saying.
        (_, string answer) = table.Begin(new TableGesture("deleteColumn", ColumnId: "nr"));

        // Assert: asked, nothing written, and the column names its other side so that the question can be put.
        Assert.Equal(KnowledgeColumnEdits.OtherSideChoice, answer);
        Assert.Equal(ours, table.Bytes());
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Equal("Cities nearby", table.Session.Baseline().Columns.Single(column => column.Id == "nr").Settings["otherSide"]);
        Assert.DoesNotContain("otherSide", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings.Keys);
    }

    [Fact]
    public async Task DeletingOneSideMadeBeforeLinks_AndTheOtherSideToo_TakesBothOutInOneStep()
    {
        // Arrange.
        await using var table = OpenMadeBeforeLinks();
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "nr", Values: ["nh", "zh"]));
        (byte[] oursWith, byte[] theirsWith) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: "nr", Settings: Settings(("otherSide", "delete"))));

        // Assert: neither side is left.
        Assert.DoesNotContain(table.OnDisk().Properties, property => property.Id == "nr");
        Assert.DoesNotContain(KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties, property => property.Id == "cn");

        // Act: one undo.
        var undone = await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: both sides are back, values and all.
        Assert.True(undone.IsSuccess);
        Assert.Equal(oursWith, table.Bytes());
        Assert.Equal(theirsWith, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingTheSideThatHoldsTheValues_AndKeepingTheOther_GivesTheOtherThoseValues()
    {
        // Arrange: Amsterdam is near two provinces, Antwerp near one of them.
        await using var table = OpenMadeBeforeLinks();
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "nr", Values: ["nh", "zh"]));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "nr", Values: ["zh"]));

        // Act.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: "nr", Settings: Settings(("otherSide", "keep"))));

        // Assert: this side is gone; the other is a relation of its own, holding what it showed.
        Assert.DoesNotContain(table.OnDisk().Properties, property => property.Id == "nr");
        var provinces = KnowledgeDocumentStore.Read(Provinces).Body!.Table;
        var nearby = provinces.Properties.Single(property => property.Name == "Cities nearby");
        Assert.Equal(("", false, "relation", "cities.yaml"), (nearby.Counterpart, nearby.IsComputed, nearby.ValueType, nearby.TargetFile));
        Assert.Equal(["r1"], provinces.ValuesOf("nh", nearby.Id));
        Assert.Equal(["r1", "r2"], provinces.ValuesOf("zh", nearby.Id));
        Assert.Null(provinces.ValuesOf("an", nearby.Id));
    }

    [Fact]
    public async Task DeletingTheSideThatShowsTheValues_AndKeepingTheOther_LeavesTheOtherItsValues()
    {
        // Arrange: both tables open, and a value on the side that holds them.
        var documents = new KnowledgeDocuments();
        await using var cities = OpenMadeBeforeLinks(documents);
        await cities.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "nr", Values: ["nh"]));
        await using var provinces = new EditingTable(Provinces, documents: documents);
        Assert.Equal("Near", provinces.ColumnSettings("cn")["otherSide"]);

        // Act: the computed side is deleted from its own table.
        await provinces.Edit(new TableGesture("deleteColumn", ColumnId: "cn", Settings: Settings(("otherSide", "keep"))));

        // Assert: the side that holds the values is a one-way relation, with its values.
        Assert.DoesNotContain(provinces.OnDisk().Properties, property => property.Id == "cn");
        var kept = KnowledgeDocumentStore.Read(cities.Path).Body!.Table;
        Assert.Equal(("", false), (kept.Properties.Single(property => property.Id == "nr").Counterpart, kept.Properties.Single(property => property.Id == "nr").IsComputed));
        Assert.Equal(["nh"], kept.Rows.Single(row => row.Id == "r1").Cells.Single(cell => cell.PropertyId == "nr").Values);
    }

    [Fact]
    public async Task ATwoWayRelationToTheTableItselfMadeBeforeLinks_IsConvertedIntoOneRelationAndItsBacklinkColumn()
    {
        // Arrange: Amsterdam is the rival of Antwerp, through the two sides a file made before links holds.
        var path = KnowledgeFiles.CopyPlainMadeBeforeLinks("cities.yaml", _root);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, text.Replace("  - id: p6\r\n", "  - id: rv\r\n    name: Rival of\r\n    type: relation\r\n    target: .\r\n    counterpart: rb\r\n  - id: rb\r\n    name: Rivalled by\r\n    type: relation\r\n    target: .\r\n    counterpart: rv\r\n    computed: true\r\n  - id: p6\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await using var table = new EditingTable(path, newDocumentId: () => "c4t7y2ma");

        // Act.
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: "rv", Values: ["r2"]));

        // Assert: one relation to the file's own id, which holds the value under the counterpart's name as its backlink name; the counterpart is gone, and its column is the backlink column now.
        var converted = table.OnDisk();
        Assert.Equal(("$c4t7y2ma@cities.yaml", "", "Rivalled by"), (converted.Properties.Single(property => property.Id == "rv").TargetFile, converted.Properties.Single(property => property.Id == "rv").Counterpart, converted.Properties.Single(property => property.Id == "rv").BacklinkName));
        Assert.DoesNotContain(converted.Properties, property => property.Id == "rb");
        var rivalled = table.Session.Baseline().Columns.Single(column => column.Name == "Rivalled by");
        Assert.Equal(["Amsterdam"], Cell(table.Lines(), "r2", rivalled.Id)?.Labels);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task WhenTheTargetIsRenamed_TheRelationNamesItByItsNewName_AndByItsOldOneWhenThatIsUndone(string extension)
    {
        // Arrange: the example as it was made before links relates to provinces.yaml beside it by its path.
        var cities = KnowledgeFiles.CopyPlainMadeBeforeLinks("cities" + extension, _root);
        var before = await File.ReadAllBytesAsync(cities, TestContext.Current.CancellationToken);
        var regions = IoPath.Combine(_root, "regions.yaml");
        var follower = new KnowledgeTargetRename(new KnowledgeDocuments(), new FileWriter());

        // Act: the target is renamed, as the explorer does it.
        File.Move(Provinces, regions);
        await follower.RenamedAsync(ProjectRoot.From(_root), Provinces, regions, TestContext.Current.CancellationToken);

        // Assert: the relation names the file where it is now, and resolves; the renamed file is untouched.
        var table = KnowledgeDocumentStore.Read(cities).Body!.Table;
        Assert.Equal("regions.yaml", table.Properties.Single(property => property.Id == "p5").TargetFile);
        await using (var open = new EditingTable(cities))
        {
            Assert.Equal(["Noord-Holland"], Cell(open.Lines(), "r1", "p5")?.Labels);
            Assert.All(open.Session.Baseline().Findings, finding => Assert.Equal(KnowledgeValidator.UnresolvedTarget, finding.Code));
        }

        // Act: the rename is undone, which is a rename back.
        File.Move(regions, Provinces);
        await follower.RenamedAsync(ProjectRoot.From(_root), regions, Provinces, TestContext.Current.CancellationToken);

        // Assert: byte for byte what it was.
        Assert.Equal(before, await File.ReadAllBytesAsync(cities, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenALinkedTargetIsRenamedOrMoved_NothingIsWritten_AndTheRelationStillFindsIt()
    {
        // Arrange: the example links to its provinces by their document id.
        var cities = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        var before = await File.ReadAllBytesAsync(cities, TestContext.Current.CancellationToken);
        var moved = IoPath.Combine(_root, "reference", "regions.yaml");
        Directory.CreateDirectory(IoPath.GetDirectoryName(moved)!);

        // Act: renamed and moved, as the explorer does it.
        File.Move(Provinces, moved);
        await new KnowledgeTargetRename(new KnowledgeDocuments(), new FileWriter()).RenamedAsync(ProjectRoot.From(_root), Provinces, moved, TestContext.Current.CancellationToken);

        // Assert: the file is as it was, and its relation finds the provinces by their id.
        Assert.Equal(before, await File.ReadAllBytesAsync(cities, TestContext.Current.CancellationToken));
        await using var open = new EditingTable(cities, rootPath: _root);
        Assert.Equal(["Noord-Holland"], Cell(open.Lines(), "r1", "p5")?.Labels);
        Assert.Empty(open.Session.Baseline().Findings);
    }

    [Fact]
    public async Task WhenAFolderIsRenamed_RelationsIntoItFollow_AndRelationsInsideItStay()
    {
        // Arrange: cities, made before links, relates to a table in a folder; a table in that folder relates to its neighbour there.
        var cities = KnowledgeFiles.CopyPlainMadeBeforeLinks("cities.yaml", _root);
        (string old, string renamed) = (IoPath.Combine(_root, "data"), IoPath.Combine(_root, "reference"));
        Directory.CreateDirectory(old);
        File.Move(Provinces, IoPath.Combine(old, KnowledgeFiles.Related));
        var text = await File.ReadAllTextAsync(cities, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(cities, text.Replace("target: provinces.yaml", "target: data/provinces.yaml", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(old, "inside.yaml"), text, TestContext.Current.CancellationToken);
        Assert.Equal("data/provinces.yaml", KnowledgeDocumentStore.Read(cities).Body!.Table.Properties.Single(property => property.Id == "p5").TargetFile);

        // Act.
        Directory.Move(old, renamed);
        await new KnowledgeTargetRename(new KnowledgeDocuments(), new FileWriter()).RenamedAsync(ProjectRoot.From(_root), old, renamed, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("reference/provinces.yaml", KnowledgeDocumentStore.Read(cities).Body!.Table.Properties.Single(property => property.Id == "p5").TargetFile);
        Assert.Equal(text, await File.ReadAllTextAsync(IoPath.Combine(renamed, "inside.yaml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingARowOtherTablesRelateTo_TellsHowManyFirst_AndDeletesWhenConfirmed()
    {
        // Arrange: in the example, Amsterdam relates to the province nh; a second city is made to as well.
        var cities = KnowledgeFiles.CopyPlain("cities.yaml", _root);
        await using (var edited = new EditingTable(cities))
        {
            await edited.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: "p5", Values: ["nh"]));
        }

        await using var provinces = new EditingTable(Provinces, rootPath: _root);
        var before = provinces.Bytes();

        // Act: the row is deleted without more.
        (_, string asked) = provinces.Begin(new TableGesture("deleteRows", RowId: "nh"));

        // Assert: a question that says how many, and nothing deleted.
        Assert.Equal("confirm:2 rows in other tables relate to this row. Deleting it leaves those relations pointing at nothing.", asked);
        Assert.Equal(before, provinces.Bytes());

        // Act: a row nobody relates to is deleted at once; the other one when confirmed.
        await provinces.Edit(new TableGesture("deleteRows", RowId: "an"));
        await provinces.Edit(new TableGesture("deleteRows", RowId: "nh", Settings: Settings(("confirmed", "true"))));

        // Assert.
        Assert.Equal(["zh", "9wvrdvjn4ob2f2nw421bouq7y", "cdns3i0ehcr45q7zh0z15iqzt"], provinces.OnDisk().Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task WhenTheOtherFileRefuses_NeitherFileIsWritten()
    {
        // Arrange: the deletion of both sides of a relation made before links is accepted and shown, and waits to be written.
        await using var table = OpenMadeBeforeLinks();
        var ours = table.Bytes();
        table.Hold();
        (ShortGuid edit, string answer) = table.Begin(new TableGesture("deleteColumn", ColumnId: "nr", Settings: Settings(("otherSide", "delete"))));
        Assert.Equal("", answer);

        // Act: before it is, the other file becomes one this application does not change.
        var text = await File.ReadAllTextAsync(Provinces, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Provinces, text.Replace("ded: \"0.1\"", "ded: \"9.0\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var theirs = await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken);
        table.Release();
        var settled = await table.Settled(edit);

        // Assert: refused with the other file's name, taken back, and neither file written.
        Assert.False(settled.Written);
        Assert.StartsWith(KnowledgeFiles.Related + " cannot be changed: ", settled.Error, StringComparison.Ordinal);
        Assert.Equal(ours, table.Bytes());
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Contains(table.Session.Baseline().Columns, column => column.Name == "Near");
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARelationIsAProperty_AskedForByName_WithItsTableChosenFromTheProjectsTables(string extension)
    {
        // Arrange: the table knows the project it is in.
        await using var table = new EditingTable(KnowledgeFiles.CopyPlain("cities" + extension, _root), rootPath: _root);
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: asked for by its name alone.
        (_, string asked) = table.Begin(new TableGesture("addRelation", Values: ["Region"]));

        // Assert: nothing is made; the answer lists this table first, then the project's other table under its own name.
        Assert.Equal("""choose:[{"id":".","label":"This table"},{"id":"provinces.yaml","label":"Provinces"}]""", asked);
        Assert.Equal(ours, table.Bytes());

        // Act: the table chosen, two-way with no name for the other side, left of the population.
        await table.Edit(new TableGesture("addRelation", TargetId: "p3", Values: ["Region"], Settings: Settings(("target", "provinces.yaml"), ("limit", "one"), ("twoWay", "true"), ("side", "left"))));

        // Assert: the property is there under its name, where it was put; its backlink is called after this table, and the provinces are not written.
        var properties = table.OnDisk().Properties;
        var region = properties.Single(property => property.Name == "Region");
        Assert.Equal(("relation", KnowledgeFiles.RelatedLink, "one", false, "Cities"), (region.ValueType, region.TargetFile, region.Limit, region.IsComputed, region.BacklinkName));
        Assert.Equal("p3", properties[properties.ToList().IndexOf(region) + 1].Id);
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARelationsLimitAndItsOtherSide_AreSetOnTheProperty_AndNoRowChanges(string extension)
    {
        // Arrange: the province of a city, which holds one row and is shown in this table only.
        await using var table = Open(extension);
        Assert.Equal(("one", ""), (table.OnDisk().Property("p5").Limit, table.OnDisk().Property("p5").Counterpart));
        var rows = KnowledgeFiles.Describe(table.OnDisk() with { Properties = [], Views = [] });
        var theirs = KnowledgeFiles.Describe(KnowledgeDocumentStore.Read(Provinces).Body!.Table);

        // Act: any number of rows, then one again; saying what it is already changes nothing.
        await table.Edit(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "none"))));
        Assert.Equal("none", table.OnDisk().Property("p5").Limit);
        Assert.Equal("none", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings["limit"]);
        var bytes = table.Bytes();
        (_, string same) = table.Begin(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "none"))));
        Assert.Equal("", same);
        Assert.Equal(bytes, table.Bytes());
        await table.Edit(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "one"))));
        Assert.Equal("one", table.OnDisk().Property("p5").Limit);

        // Act: shown in the provinces too, under a name.
        await table.Edit(new TableGesture("setRelationOtherSide", ColumnId: "p5", Settings: Settings(("twoWay", "true"), ("counterpart", "Towns"))));

        // Assert: this side names its backlink, and the provinces are not written.
        Assert.Equal("Towns", table.OnDisk().Property("p5").BacklinkName);
        Assert.Equal("Towns", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings["backlinkName"]);
        Assert.Equal(theirs, KnowledgeFiles.Describe(KnowledgeDocumentStore.Read(Provinces).Body!.Table));

        // Act: in this table only again.
        await table.Edit(new TableGesture("setRelationOtherSide", ColumnId: "p5", Settings: Settings(("twoWay", "false"))));

        // Assert: this side names no backlink, and the provinces are what they were.
        Assert.Equal("", table.OnDisk().Property("p5").BacklinkName);
        Assert.Equal(theirs, KnowledgeFiles.Describe(KnowledgeDocumentStore.Read(Provinces).Body!.Table));

        // Through all of it no row changed.
        Assert.Equal(rows, KnowledgeFiles.Describe(table.OnDisk() with { Properties = [], Views = [] }));

        // Neither is set on a property that is not a relation, and the parent relation keeps its one row.
        bytes = table.Bytes();
        (_, string number) = table.Begin(new TableGesture("setRelationLimit", ColumnId: "p3", Settings: Settings(("limit", "one"))));
        (_, string side) = table.Begin(new TableGesture("setRelationOtherSide", ColumnId: "p3", Settings: Settings(("twoWay", "true"))));
        Assert.Equal((KnowledgeColumnEdits.NotARelation, KnowledgeColumnEdits.NotARelation), (number, side));
        Assert.Equal(bytes, table.Bytes());
        await table.Edit(new TableGesture("addRelation", Values: ["Part of"], Settings: Settings(("target", KnowledgeRelations.Self), ("limit", "one"))));
        var partOf = Property(table, "Part of").Id;
        await table.Edit(new TableGesture("setColumnParent", ColumnId: partOf, Settings: Settings(("parent", "true"))));
        (_, string parent) = table.Begin(new TableGesture("setRelationLimit", ColumnId: partOf, Settings: Settings(("limit", "none"))));
        Assert.Equal(KnowledgeColumnEdits.ParentHoldsOne, parent);
    }

    [Fact]
    public async Task ARelation_NeedsATableToRelateTo()
    {
        // Arrange.
        await using var table = Open();
        var before = table.Bytes();

        // Act.
        (_, string none) = table.Begin(new TableGesture("addRelation"));
        (_, string missing) = table.Begin(new TableGesture("addRelation", Settings: Settings(("target", "nowhere.yaml"))));

        // Assert: asked for without a table, it is answered with what there is to choose from - here, where no project is known, the table itself.
        Assert.Equal("""choose:[{"id":".","label":"This table"}]""", none);
        Assert.Equal("'nowhere.yaml' is not there.", missing);
        Assert.Equal(before, table.Bytes());
    }

    // ---- the table itself, and the parent relation ----------------------------------------------------------

    [Fact]
    public async Task ATwoWayRelationToTheTableItself_ShowsItsBacklinksInTheSameTable_FromOneProperty()
    {
        // Arrange.
        await using var table = Open();

        // Act.
        await table.Edit(new TableGesture("addRelation", Values: ["Rival of"], Settings: Settings(("target", KnowledgeRelations.Self), ("counterpart", "Rivalled by"))));
        var rival = Property(table, "Rival of");
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: rival.Id, Values: ["r2"]));

        // Assert: one property, which holds the value; the backlink column shows it from the other end, and is in no file.
        Assert.Equal((KnowledgeFiles.SelfLink(".yaml"), "", "Rivalled by"), (rival.TargetFile, rival.Counterpart, rival.BacklinkName));
        Assert.DoesNotContain(table.OnDisk().Properties, property => property.Name == "Rivalled by");
        var rivalled = table.Session.Baseline().Columns.Single(column => column.Name == "Rivalled by");
        var lines = table.Lines();
        Assert.Equal(["Antwerp"], Cell(lines, "r1", rival.Id)?.Labels);
        Assert.Null(Cell(lines, "r2", rival.Id));
        Assert.Equal(["r1"], Cell(lines, "r2", rivalled.Id)?.Values);
        Assert.Equal(["Amsterdam"], Cell(lines, "r2", rivalled.Id)?.Labels);
    }

    [Fact]
    public async Task OnlyARelationThatCanBeTheParentRelation_SaysItsRowsCanBeNestedByIt()
    {
        // Arrange: a relation to the table itself that holds one row, one that holds several, and the example's relation to another table.
        await using var table = Open();
        await table.Edit(new TableGesture("addRelation", Values: ["Part of"], Settings: Settings(("target", KnowledgeRelations.Self), ("limit", "one"))));
        await table.Edit(new TableGesture("addRelation", Values: ["Twinned with"], Settings: Settings(("target", KnowledgeRelations.Self))));

        // Assert.
        Assert.Equal("true", table.ColumnSettingsNamed("Part of")["nestable"]);
        Assert.DoesNotContain("nestable", table.ColumnSettingsNamed("Twinned with").Keys);
        Assert.DoesNotContain("nestable", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings.Keys);
        Assert.DoesNotContain("parent", table.ColumnSettingsNamed("Part of").Keys);

        // Act: made the parent relation, as the column's menu does.
        await table.Edit(new TableGesture("setColumnParent", ColumnId: Property(table, "Part of").Id, Settings: Settings(("parent", "true"))));

        // Assert.
        Assert.Equal("true", table.ColumnSettingsNamed("Part of")["parent"]);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARelationOfATableToItself_ShowsBothDirectionsInItsOneProperty(string extension)
    {
        // Arrange: a relation of the cities to the cities, with no other side asked for.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("addRelation", Values: ["Twinned with"], Settings: Settings(("target", KnowledgeRelations.Self))));
        var twinned = Property(table, "Twinned with").Id;

        // Act: Amsterdam is twinned with Antwerp.
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: twinned, Values: ["r2"]));

        // Assert: one of the two holds the pair, and both show it.
        Assert.Equal(["r2"], table.OnDisk().ValuesOf("r1", twinned));
        Assert.Null(table.OnDisk().ValuesOf("r2", twinned));
        var lines = table.Lines();
        Assert.Equal(["r2"], Cell(lines, "r1", twinned)?.Values);
        Assert.Equal(["r1"], Cell(lines, "r2", twinned)?.Values);
        Assert.Equal(["Amsterdam"], Cell(lines, "r2", twinned)?.Labels);

        // Act: confirmed from the other end, which changes nothing; then taken away there.
        var bytes = table.Bytes();
        (_, string same) = table.Begin(new TableGesture("setCell", RowId: "r2", ColumnId: twinned, Values: ["r1"]));
        Assert.Equal("", same);
        Assert.Equal(bytes, table.Bytes());
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: twinned, Values: []));

        // Assert: gone from the end that held it, and from both rows on the screen.
        Assert.Null(table.OnDisk().ValuesOf("r1", twinned));
        Assert.Null(table.OnDisk().ValuesOf("r2", twinned));
        Assert.Null(Cell(table.Lines(), "r1", twinned));

        // The parent relation keeps its direction: a row under another does not put that one under it.
        await table.Edit(new TableGesture("addRelation", Values: ["Part of"], Settings: Settings(("target", KnowledgeRelations.Self), ("limit", "one"))));
        var partOf = Property(table, "Part of").Id;
        await table.Edit(new TableGesture("setColumnParent", ColumnId: partOf, Settings: Settings(("parent", "true"))));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: partOf, Values: ["r1"]));
        Assert.Null(Cell(table.Lines(), "r1", partOf));
    }

    [Fact]
    public async Task TheParentRelation_NestsRows_AndNeverMakesARowItsOwnAncestor()
    {
        // Arrange: a relation to the table itself that holds one row, and a new row at the end.
        await using var table = Open();
        await table.Edit(new TableGesture("addRelation", Values: ["Part of"], Settings: Settings(("target", KnowledgeRelations.Self), ("limit", "one"))));
        var part = Property(table, "Part of").Id;
        await table.Edit(new TableGesture("addRow"));
        var third = table.OnDisk().Rows[^1].Id;

        // Act: it becomes the parent relation, and the rows get their parents.
        await table.Edit(new TableGesture("setColumnParent", ColumnId: part, Settings: Settings(("parent", "true"))));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: part, Values: ["r1"]));
        await table.Edit(new TableGesture("setCell", RowId: third, ColumnId: part, Values: ["r2"]));

        // Assert: a row is not its own parent, and not the parent of anything above it.
        Assert.Equal(KnowledgeCellEdits.ParentCycle, table.Begin(new TableGesture("setCell", RowId: "r1", ColumnId: part, Values: ["r1"])).Answer);
        Assert.Equal(KnowledgeCellEdits.ParentCycle, table.Begin(new TableGesture("setCell", RowId: "r1", ColumnId: part, Values: [third])).Answer);
        Assert.True(Property(table, "Part of").IsParent);

        // Act: a view grouped by it.
        await table.Edit(new TableGesture("groupBy", ColumnId: part, ViewId: "v2"));

        // Assert: each row under its parent, a level deeper, and the rows that have none at the top.
        Assert.Equal(["r1:0", "r2:1", $"{third}:2", .. new[] { "45ako65f2m2wasyxq3mam8ikt", "a8q7ddqb6zyubcyf5vuky6iwb", "8heh3sdyy0a63fyq7gicize5a", "7pa30fmuw51bv1vyl5jnmf5jh", "8rj09k224141g98x6rguz7f5h", "e1v5f01oi4bx1syl7x6ty3zeo", "1dytemy0l8cen9xxkpcxzlgwq", "9jh7yjm443uvm99io0nc6ekpi", "9cwydn1h89znwl1wlxblxselj" }.Select(id => $"{id}:0")], table.Lines().Where(line => !line.IsNewRow).Select(line => $"{line.Id}:{line.Depth}"));
        Assert.Empty(table.Session.Baseline().Findings);
    }

    [Fact]
    public async Task OnlyARelationToTheTableItselfThatHoldsOneRow_CanBeTheParentRelation()
    {
        // Arrange: the example's own relation points at another table.
        await using var table = Open();

        // Act.
        (_, string elsewhere) = table.Begin(new TableGesture("setColumnParent", ColumnId: "p5", Settings: Settings(("parent", "true"))));
        (_, string text) = table.Begin(new TableGesture("setColumnParent", ColumnId: "p1", Settings: Settings(("parent", "true"))));

        // Assert.
        Assert.Equal("Only a relation to this table that holds one row can be the parent relation.", elsewhere);
        Assert.Equal("Only a relation to this table that holds one row can be the parent relation.", text);
    }
}
