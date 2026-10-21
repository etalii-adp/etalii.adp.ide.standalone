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
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-relations-").FullName;

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

    private string Provinces => IoPath.Combine(_root, KnowledgeFiles.Related);

    private EditingTable Open(string extension = ".yaml", KnowledgeDocuments? documents = null) => new(KnowledgeFiles.CopyPlain("cities" + extension, _root), documents: documents);

    private static Dictionary<string, string> Settings(params (string Name, string Value)[] settings) => settings.ToDictionary(setting => setting.Name, setting => setting.Value);

    private static TableCell? Cell(IEnumerable<TableRow> lines, string rowId, string columnId) => lines.Single(line => line.Id == rowId).Cells.FirstOrDefault(cell => cell.ColumnId == columnId);

    private static KnowledgeProperty Property(EditingTable table, string name) => table.OnDisk().Properties.Single(property => property.Name == name);

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
        await File.WriteAllTextAsync(path, text.Replace("- row: nh", "- row: atlantis", StringComparison.Ordinal), TestContext.Current.CancellationToken);
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
        Assert.Equal(("relation", KnowledgeFiles.Related, "none", "", false), (relation.ValueType, relation.TargetFile, relation.Limit, relation.Counterpart, relation.IsComputed));
        Assert.Equal(target, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Empty(table.Session.Baseline().Findings);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ATwoWayRelation_IsMadeInBothFilesInOneStep_AndUndoneInOne(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        await table.Edit(new TableGesture("addRelation", Values: ["Seat of"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Seats"))));

        // Assert: each side names the other; the other side is computed, has no cells, and points back at this file.
        var here = Property(table, "Seat of");
        var there = KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties.Single(property => property.Name == "Seats");
        Assert.Equal((there.Id, false), (here.Counterpart, here.IsComputed));
        Assert.Equal((here.Id, true, "relation", "cities" + extension), (there.Counterpart, there.IsComputed, there.ValueType, there.TargetFile));
        Assert.All(KnowledgeDocumentStore.Read(Provinces).Body!.Table.Rows, row => Assert.DoesNotContain(row.Cells, cell => cell.PropertyId == there.Id));

        // Act: one undo.
        var undone = await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: both files as they were.
        Assert.True(undone.IsSuccess);
        Assert.Equal(ours, table.Bytes());
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: and one redo.
        var redone = await table.History.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redone.IsSuccess);
        Assert.Contains(KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties, property => property.Name == "Seats");
        Assert.Contains(table.OnDisk().Properties, property => property.Name == "Seat of");
    }

    [Fact]
    public async Task TheOtherSide_ShowsTheRowsThatNameIt_AndIsNotFilledInFromThere()
    {
        // Arrange: a two-way relation, and both tables open.
        var documents = new KnowledgeDocuments();
        await using var cities = Open(documents: documents);
        await cities.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(cities, "Near").Id;
        await using var provinces = new EditingTable(Provinces, documents: documents);
        var nearby = provinces.Session.Baseline().Columns.Single(column => column.Name == "Cities nearby");

        // Act: values are given on the side that holds them.
        await cities.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near, Values: ["nh", "zh"]));
        await cities.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: near, Values: ["zh"]));

        // Assert: the other side shows them at once, by title, and has nothing of its own in its file.
        var lines = provinces.Lines();
        Assert.Equal(["r1"], Cell(lines, "nh", nearby.Id)?.Values);
        Assert.Equal(["r1", "r2"], Cell(lines, "zh", nearby.Id)?.Values);
        Assert.Equal(["Amsterdam", "Antwerp"], Cell(lines, "zh", nearby.Id)?.Labels);
        Assert.Null(Cell(lines, "an", nearby.Id));
        Assert.Equal("true", nearby.Settings["computed"]);
        Assert.All(provinces.OnDisk().Rows, row => Assert.DoesNotContain(row.Cells, cell => cell.PropertyId == nearby.Id));

        // Act: it is not filled in from here.
        (_, string answer) = provinces.Begin(new TableGesture("setCell", RowId: "an", ColumnId: nearby.Id, Values: ["r2"]));

        // Assert.
        Assert.Equal("This side of the relation is filled in from the other table.", answer);
    }

    [Fact]
    public async Task DeletingOneSideOfATwoWayRelation_AsksWhatBecomesOfTheOtherSide()
    {
        // Arrange: a two-way relation between the two files.
        await using var table = Open();
        await table.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(table, "Near").Id;
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: deleted without saying.
        (_, string answer) = table.Begin(new TableGesture("deleteColumn", ColumnId: near));

        // Assert: asked, nothing written, and the column names its other side so that the question can be put.
        Assert.Equal(KnowledgeColumnEdits.OtherSideChoice, answer);
        Assert.Equal(ours, table.Bytes());
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        Assert.Equal("Cities nearby", table.Session.Baseline().Columns.Single(column => column.Id == near).Settings["otherSide"]);
        Assert.DoesNotContain("otherSide", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings.Keys);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task DeletingOneSide_AndTheOtherSideToo_TakesBothOutInOneStep(string extension)
    {
        // Arrange.
        await using var table = Open(extension);
        (byte[] ours, byte[] theirs) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
        await table.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(table, "Near").Id;
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near, Values: ["nh", "zh"]));
        (byte[] oursWith, byte[] theirsWith) = (table.Bytes(), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: near, Settings: Settings(("otherSide", "delete"))));

        // Assert: both files as they were before the relation.
        Assert.Equal(ours, table.Bytes());
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: one undo.
        var undone = await table.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: both sides are back, values and all.
        Assert.True(undone.IsSuccess);
        Assert.Equal(oursWith, table.Bytes());
        Assert.Equal(theirsWith, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task DeletingTheSideThatHoldsTheValues_AndKeepingTheOther_GivesTheOtherThoseValues(string extension)
    {
        // Arrange: Amsterdam is near two provinces, Antwerp near one of them.
        await using var table = Open(extension);
        await table.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(table, "Near").Id;
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near, Values: ["nh", "zh"]));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: near, Values: ["zh"]));

        // Act.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: near, Settings: Settings(("otherSide", "keep"))));

        // Assert: this side is gone; the other is a relation of its own, holding what it showed.
        Assert.DoesNotContain(table.OnDisk().Properties, property => property.Id == near);
        var provinces = KnowledgeDocumentStore.Read(Provinces).Body!.Table;
        var nearby = provinces.Properties.Single(property => property.Name == "Cities nearby");
        Assert.Equal(("", false, "relation", "cities" + extension), (nearby.Counterpart, nearby.IsComputed, nearby.ValueType, nearby.TargetFile));
        Assert.Equal(["r1"], Held("nh"));
        Assert.Equal(["r1", "r2"], Held("zh"));
        Assert.Null(Held("an"));
        return;

        IReadOnlyList<string>? Held(string rowId) => provinces.Rows.Single(row => row.Id == rowId).Cells.FirstOrDefault(cell => cell.PropertyId == nearby.Id)?.Values;
    }

    [Fact]
    public async Task DeletingTheSideThatShowsTheValues_AndKeepingTheOther_LeavesTheOtherItsValues()
    {
        // Arrange: both tables open, and a value on the side that holds them.
        var documents = new KnowledgeDocuments();
        await using var cities = Open(documents: documents);
        await cities.Edit(new TableGesture("addRelation", Values: ["Near"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Cities nearby"))));
        var near = Property(cities, "Near").Id;
        await cities.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: near, Values: ["nh"]));
        await using var provinces = new EditingTable(Provinces, documents: documents);
        var nearby = provinces.Session.Baseline().Columns.Single(column => column.Name == "Cities nearby");
        Assert.Equal("Near", nearby.Settings["otherSide"]);

        // Act: the computed side is deleted from its own table.
        await provinces.Edit(new TableGesture("deleteColumn", ColumnId: nearby.Id, Settings: Settings(("otherSide", "keep"))));

        // Assert: the side that holds the values is a one-way relation, with its values.
        Assert.DoesNotContain(provinces.OnDisk().Properties, property => property.Id == nearby.Id);
        var kept = KnowledgeDocumentStore.Read(cities.Path).Body!.Table;
        Assert.Equal(("", false), (kept.Properties.Single(property => property.Id == near).Counterpart, kept.Properties.Single(property => property.Id == near).IsComputed));
        Assert.Equal(["nh"], kept.Rows.Single(row => row.Id == "r1").Cells.Single(cell => cell.PropertyId == near).Values);
    }

    [Fact]
    public async Task DeletingOneSideOfARelationToTheTableItself_KeepsTheOtherInTheSameFile()
    {
        // Arrange.
        await using var table = Open();
        await table.Edit(new TableGesture("addRelation", Values: ["Rival of"], Settings: Settings(("target", KnowledgeRelations.Self), ("counterpart", "Rivalled by"))));
        (string rival, string rivalled) = (Property(table, "Rival of").Id, Property(table, "Rivalled by").Id);
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: rival, Values: ["r2"]));

        // Act.
        await table.Edit(new TableGesture("deleteColumn", ColumnId: rival, Settings: Settings(("otherSide", "keep"))));

        // Assert: what the other side showed is now what it holds, and the table shows it as before.
        var kept = table.OnDisk();
        Assert.DoesNotContain(kept.Properties, property => property.Id == rival);
        Assert.Equal(("", false), (kept.Properties.Single(property => property.Id == rivalled).Counterpart, kept.Properties.Single(property => property.Id == rivalled).IsComputed));
        Assert.Equal(["r1"], kept.Rows.Single(row => row.Id == "r2").Cells.Single(cell => cell.PropertyId == rivalled).Values);
        Assert.Equal(["Amsterdam"], Cell(table.Lines(), "r2", rivalled)?.Labels);
        Assert.DoesNotContain("otherSide", table.Session.Baseline().Columns.Single(column => column.Id == rivalled).Settings.Keys);
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task WhenTheTargetIsRenamed_TheRelationNamesItByItsNewName_AndByItsOldOneWhenThatIsUndone(string extension)
    {
        // Arrange: the example relates to provinces.yaml beside it; a table elsewhere in the project relates to nothing.
        var cities = KnowledgeFiles.CopyPlain("cities" + extension, _root);
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
            Assert.Empty(open.Session.Baseline().Findings);
        }

        // Act: the rename is undone, which is a rename back.
        File.Move(regions, Provinces);
        await follower.RenamedAsync(ProjectRoot.From(_root), regions, Provinces, TestContext.Current.CancellationToken);

        // Assert: byte for byte what it was.
        Assert.Equal(before, await File.ReadAllBytesAsync(cities, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenAFolderIsRenamed_RelationsIntoItFollow_AndRelationsInsideItStay()
    {
        // Arrange: cities relates to a table in a folder; a table in that folder relates to its neighbour there.
        var cities = KnowledgeFiles.CopyPlain("cities.yaml", _root);
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
        // Arrange: the edit is accepted and shown, and waits to be written.
        await using var table = Open();
        var ours = table.Bytes();
        table.Hold();
        (ShortGuid edit, string answer) = table.Begin(new TableGesture("addRelation", Values: ["Seat of"], Settings: Settings(("target", KnowledgeFiles.Related), ("counterpart", "Seats"))));
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
        Assert.DoesNotContain(table.Session.Baseline().Columns, column => column.Name == "Seat of");
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

        // Assert: the property is there under its name, where it was put; the other side is called after this table.
        var properties = table.OnDisk().Properties;
        var region = properties.Single(property => property.Name == "Region");
        Assert.Equal(("relation", "provinces.yaml", "one", false), (region.ValueType, region.TargetFile, region.Limit, region.IsComputed));
        Assert.Equal("p3", properties[properties.ToList().IndexOf(region) + 1].Id);
        var other = KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties.Single(property => property.Id == region.Counterpart);
        Assert.Equal(("Cities", true, region.Id), (other.Name, other.IsComputed, other.Counterpart));
        Assert.NotEqual(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public async Task ARelationsLimitAndItsOtherSide_AreSetOnTheProperty_AndNoRowChanges(string extension)
    {
        // Arrange: the province of a city, which holds one row and is shown in this table only.
        await using var table = Open(extension);
        Assert.Equal(("one", ""), (Province().Limit, Province().Counterpart));
        var rows = KnowledgeFiles.Describe(table.OnDisk() with { Properties = [], Views = [] });
        var theirs = KnowledgeFiles.Describe(KnowledgeDocumentStore.Read(Provinces).Body!.Table);

        // Act: any number of rows, then one again; saying what it is already changes nothing.
        await table.Edit(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "none"))));
        Assert.Equal("none", Province().Limit);
        Assert.Equal("none", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings["limit"]);
        var bytes = table.Bytes();
        (_, string same) = table.Begin(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "none"))));
        Assert.Equal("", same);
        Assert.Equal(bytes, table.Bytes());
        await table.Edit(new TableGesture("setRelationLimit", ColumnId: "p5", Settings: Settings(("limit", "one"))));
        Assert.Equal("one", Province().Limit);

        // Act: shown in the provinces too, under a name.
        await table.Edit(new TableGesture("setRelationOtherSide", ColumnId: "p5", Settings: Settings(("twoWay", "true"), ("counterpart", "Towns"))));

        // Assert: the other side is there, shows this side's values and holds none; this side names it.
        var other = KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties.Single(property => property.Name == "Towns");
        Assert.Equal((true, "p5", "relation"), (other.IsComputed, other.Counterpart, other.ValueType));
        Assert.Equal(other.Id, Province().Counterpart);
        Assert.Equal("Towns", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings["otherSide"]);

        // Act: in this table only again.
        await table.Edit(new TableGesture("setRelationOtherSide", ColumnId: "p5", Settings: Settings(("twoWay", "false"))));

        // Assert: the other side is gone, this side names none, and the provinces are what they were.
        Assert.Equal("", Province().Counterpart);
        Assert.DoesNotContain(KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties, property => property.Name == "Towns");
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
        return;

        KnowledgeProperty Province() => table.OnDisk().Properties.Single(property => property.Id == "p5");
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
    public async Task ATwoWayRelationToTheTableItself_HasBothSidesInTheOneFile()
    {
        // Arrange.
        await using var table = Open();

        // Act.
        await table.Edit(new TableGesture("addRelation", Values: ["Rival of"], Settings: Settings(("target", KnowledgeRelations.Self), ("counterpart", "Rivalled by"))));
        (KnowledgeProperty rival, KnowledgeProperty rivalled) = (Property(table, "Rival of"), Property(table, "Rivalled by"));
        await table.Edit(new TableGesture("setCell", RowId: "r1", ColumnId: rival.Id, Values: ["r2"]));

        // Assert: one side holds the value, the other shows it from the other end.
        Assert.Equal((KnowledgeRelations.Self, rivalled.Id), (rival.TargetFile, rival.Counterpart));
        Assert.Equal((KnowledgeRelations.Self, rival.Id, true), (rivalled.TargetFile, rivalled.Counterpart, rivalled.IsComputed));
        var lines = table.Lines();
        Assert.Equal(["Antwerp"], Cell(lines, "r1", rival.Id)?.Labels);
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
        Assert.Equal("true", Of("Part of")["nestable"]);
        Assert.DoesNotContain("nestable", Of("Twinned with").Keys);
        Assert.DoesNotContain("nestable", table.Session.Baseline().Columns.Single(column => column.Id == "p5").Settings.Keys);
        Assert.DoesNotContain("parent", Of("Part of").Keys);

        // Act: made the parent relation, as the column's menu does.
        await table.Edit(new TableGesture("setColumnParent", ColumnId: Property(table, "Part of").Id, Settings: Settings(("parent", "true"))));

        // Assert.
        Assert.Equal("true", Of("Part of")["parent"]);
        return;

        IReadOnlyDictionary<string, string> Of(string name) => table.Session.Baseline().Columns.Single(column => column.Name == name).Settings;
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
        Assert.Equal(["r2"], Held("r1"));
        Assert.Null(Held("r2"));
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
        Assert.Null(Held("r1"));
        Assert.Null(Held("r2"));
        Assert.Null(Cell(table.Lines(), "r1", twinned));

        // The parent relation keeps its direction: a row under another does not put that one under it.
        await table.Edit(new TableGesture("addRelation", Values: ["Part of"], Settings: Settings(("target", KnowledgeRelations.Self), ("limit", "one"))));
        var partOf = Property(table, "Part of").Id;
        await table.Edit(new TableGesture("setColumnParent", ColumnId: partOf, Settings: Settings(("parent", "true"))));
        await table.Edit(new TableGesture("setCell", RowId: "r2", ColumnId: partOf, Values: ["r1"]));
        Assert.Null(Cell(table.Lines(), "r1", partOf));
        return;

        IReadOnlyList<string>? Held(string rowId) => table.OnDisk().Rows.Single(row => row.Id == rowId).Cells.FirstOrDefault(cell => cell.PropertyId == twinned)?.Values;
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
