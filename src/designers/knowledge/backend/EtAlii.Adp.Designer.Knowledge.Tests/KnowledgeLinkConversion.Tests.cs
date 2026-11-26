using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The conversion of a knowledge file made before links (knowledge.md, section 9; etalii.adp spec 092, T028): given its id,
/// its relations and cells as links, its counterparts' names as backlink names, written with its first edit and in it alone.
/// The subject is etalii.adp's fixture <c>specifications/fbl/fixtures/links-workspace/knowledge/</c>, whose
/// <c>cities.converted</c> is what converting <c>cities.yaml</c> with the id <c>c1t9e5sx</c> gives.
/// </summary>
public sealed class KnowledgeLinkConversionTests : IDisposable
{
    private const string GivenId = "c1t9e5sx";

    private static readonly string _fixture = RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance", "etalii.adp", "specifications", "fbl", "fixtures", "links-workspace", "knowledge");

    private readonly TestFolder _testFolder = TestFolder.Create("adp-knowledge-conversion");
    private readonly string _folder;

    public KnowledgeLinkConversionTests()
    {
        _folder = IoPath.Combine(_testFolder.Path, "knowledge");
        Directory.CreateDirectory(_folder);
        foreach (var file in Directory.EnumerateFiles(_fixture))
        {
            File.Copy(file, IoPath.Combine(_folder, IoPath.GetFileName(file)));
        }
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private string Cities => IoPath.Combine(_folder, "cities.yaml");

    private string Provinces => IoPath.Combine(_folder, "provinces.yaml");

    /// <summary>What a relation of the file at <paramref name="bodyPath"/> points at, read from disk.</summary>
    private static Func<KnowledgeProperty, KnowledgeTarget?> TargetsOf(string bodyPath) => property =>
    {
        var path = KnowledgeRelations.TargetPath(bodyPath, property.TargetFile);
        return new KnowledgeTarget(path, KnowledgeDocumentStore.Read(path).Body?.Table);
    };

    /// <summary>The bytes converting the file at <paramref name="path"/> gives, with <see cref="GivenId"/> where it has no id.</summary>
    private static byte[] Converted(string path)
    {
        var body = KnowledgeDocumentStore.Read(path).Body!;
        var changes = KnowledgeLinkConversion.Of(body.Table, path, TargetsOf(path), () => GivenId);
        (byte[]? bytes, string refusal) = body.Change(changes);
        Assert.True(bytes is not null, refusal);
        return bytes;
    }

    /// <summary>Each row with the rows its relation cells name, by property: what a conversion keeps.</summary>
    private static List<string> Pairs(KnowledgeTable table) =>
    [
        .. table.Rows.SelectMany(row => row.Cells.Where(cell => cell.Key == "rows").SelectMany(cell => cell.Values.Select(value => $"{row.Id}/{cell.PropertyId} -> {value}"))).Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void AFileMadeBeforeLinks_IsConvertedToTheFixturesConvertedFile_ByteForByte()
    {
        // Act.
        var converted = Converted(Cities);

        // Assert: its id beside the envelope, the version raised, the relations to provinces and to itself and every cell as links, the counterpart's name as the backlink name, and the relation to a file that is not there as it was.
        Assert.Equal(File.ReadAllBytes(IoPath.Combine(_folder, "cities.converted")), converted);
    }

    [Fact]
    public void AConversion_KeepsEveryRelatedRow()
    {
        // Arrange.
        var before = Pairs(KnowledgeDocumentStore.Read(Cities).Body!.Table);

        // Act.
        File.WriteAllBytes(Cities, Converted(Cities));

        // Assert: as many links after as there were ids before, to the same rows (SC-005).
        Assert.Equal(before, Pairs(KnowledgeDocumentStore.Read(Cities).Body!.Table));
    }

    [Fact]
    public void ACounterpart_IsKeptWhileTheRelationNamingItIsMadeBeforeLinks_AndRemovedOnceItIsNot()
    {
        // Arrange: the provinces hold an id and a counterpart, Cities, of the cities' relation.
        var provinces = File.ReadAllBytes(Provinces);

        // Act, assert: nothing to convert while the cities are as they were.
        Assert.Equal(provinces, Converted(Provinces));

        // Act: the cities converted.
        File.WriteAllBytes(Cities, Converted(Cities));

        // Assert: the counterpart goes, since the target computes that column from the cities' backlinks now.
        var after = KnowledgeDocumentStore.Read(Provinces).Body!.Table;
        File.WriteAllBytes(Provinces, Converted(Provinces));
        Assert.Contains(after.Properties, property => property.Name == "Cities");
        Assert.DoesNotContain(KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties, property => property.Name == "Cities");
    }

    [Fact]
    public async Task TheConversion_IsShownOnOpening_WrittenWithTheFirstEdit_InThisFileAlone_AndUndoneWithIt()
    {
        // Arrange.
        var cities = await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken);
        var provinces = await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken);
        var converted = await File.ReadAllTextAsync(IoPath.Combine(_folder, "cities.converted"), TestContext.Current.CancellationToken);
        await using var table = new EditingTable(Cities, rootPath: _testFolder.Path, newDocumentId: () => GivenId);

        // Assert: opening writes nothing, and shows the relations as links already; the one whose target is not there says so.
        Assert.Equal(cities, table.Bytes());
        Assert.Equal("$k3f9x2ma@provinces.yaml", table.Session.Shown.Properties.Single(property => property.Id == "p2").TargetFile);
        Assert.Contains(table.Session.Baseline().Findings, finding => finding is { Code: KnowledgeValidator.UnresolvedTarget, ColumnId: "p4" });

        // Act: an edit of something else.
        await table.Edit(new TableGesture("setCell", RowId: "utr", ColumnId: "p1", Values: ["Utrecht (city)"]));

        // Assert: the edit and the conversion, in this file only.
        Assert.Equal(converted.Replace("text: Utrecht\r\n", "text: Utrecht (city)\r\n", StringComparison.Ordinal).Replace("text: Utrecht\n", "text: Utrecht (city)\n", StringComparison.Ordinal), System.Text.Encoding.UTF8.GetString(table.Bytes()));
        Assert.Equal(provinces, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: undone.
        Assert.True((await table.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);

        // Assert: the file as it was made.
        Assert.Equal(cities, table.Bytes());
    }

    [Fact]
    public void TheLinkIndex_HoldsAKnowledgeFileByItsRows_NamedByItsTitleProperty_AndByTheLinksItsRelationsHold()
    {
        // Arrange.
        var indexer = new KnowledgeLinkIndexer();
        var converted = IoPath.Combine(_folder, "cities.converted.yaml");
        File.WriteAllBytes(converted, File.ReadAllBytes(IoPath.Combine(_folder, "cities.converted")));

        // Act.
        var document = indexer.Index(converted, File.ReadAllBytes(converted));
        var notKnowledge = indexer.Index(IoPath.Combine(_folder, "notes.yaml"), "title: notes\n"u8.ToArray());

        // Assert: the id, the rows by their names, and each link with what holds it: a relation's target, a cell's row.
        Assert.NotNull(document);
        Assert.Equal((GivenId, "Name", Designer.Origin), (document.DocumentId, document.NameProperty, document.Origin));
        Assert.Equal("Utrecht", document.Elements.Single(element => element.Id == "utr").ValueOf("Name"));
        Assert.Contains(new Specification.Fbl.Links.HeldLink("p2", "$k3f9x2ma@provinces.yaml"), document.Links);
        Assert.Contains(new Specification.Fbl.Links.HeldLink("ams/p2", "$k3f9x2ma@provinces.yaml#nh.Name=Noord-Holland"), document.Links);
        Assert.Contains(new Specification.Fbl.Links.HeldLink("haa/p3", "$c1t9e5sx@cities.yaml#gone"), document.Links);
        Assert.DoesNotContain(document.Links, link => link.Text.Contains("nowhere", StringComparison.Ordinal));
        Assert.Null(notKnowledge);
        Assert.True(indexer.Claims("a/b.YAML") && indexer.Claims("c.xml") && !indexer.Claims("d.tml"));

        // Act: a new id, as the fix of an ambiguous document gives one.
        var renewed = indexer.Renewed(converted, File.ReadAllBytes(converted));

        // Assert.
        Assert.NotNull(renewed);
        var id = KnowledgeBody.Read(renewed, converted).Table.DocumentId;
        Assert.Matches("^[0-9a-z]{8}$", id);
        Assert.NotEqual(GivenId, id);
    }

    [Fact]
    public async Task ARelationThatLinksToItsTable_TellsTheClientWhereThatFileWasFound()
    {
        // Arrange: the provinces in a folder of their own; the converted cities link to them by their id.
        await File.WriteAllBytesAsync(Cities, Converted(Cities), TestContext.Current.CancellationToken);
        var moved = IoPath.Combine(_testFolder.Path, "reference", "provinces.yaml");
        Directory.CreateDirectory(IoPath.GetDirectoryName(moved)!);
        File.Move(Provinces, moved);

        // Act.
        await using var table = new EditingTable(Cities, rootPath: _testFolder.Path);
        var settings = table.ColumnSettings("p2");

        // Assert: named by the link, opened by its path from here.
        Assert.Equal(("$k3f9x2ma@provinces.yaml", "../reference/provinces.yaml"), (settings["targetFile"], settings["targetPath"]));
        Assert.DoesNotContain("targetPath", table.ColumnSettings("p3").Keys);
    }

    /// <summary>The standalone examples as they were made before links: what converting them is held to.</summary>
    private static readonly string _madeBeforeLinks = RepositoryPaths.Under("src", "designers", "knowledge", "backend", "EtAlii.Adp.Designer.Knowledge.Tests", "Fixtures", "made-before-links");

    /// <summary>
    /// Each standalone example as it was made before links, converted file by file in the order its tables are first edited,
    /// with the id it holds now: the example as it is shipped, byte for byte, with every (row, related row) pair it had.
    /// </summary>
    [Fact]
    public void TheExamplesMadeBeforeLinks_AreConvertedToTheExamplesAsTheyAreShipped_KeepingEveryRelatedRow()
    {
        // Arrange: a copy of the examples as they were.
        var folder = IoPath.Combine(_testFolder.Path, "before");
        foreach (var file in Directory.EnumerateFiles(_madeBeforeLinks, "*", SearchOption.AllDirectories))
        {
            var copy = IoPath.Combine(folder, IoPath.GetRelativePath(_madeBeforeLinks, file));
            Directory.CreateDirectory(IoPath.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        (string File, string Id, string Shipped)[] steps =
        [
            ("provinces.yaml", "p7v3k9qd", IoPath.Combine(KnowledgeFiles.ExamplesFolder, "provinces.yaml")),
            ("cities.yaml", "c4t7y2ma", IoPath.Combine(KnowledgeFiles.ExamplesFolder, "cities.yaml")),
            ("cities.json", "c9j3s6on", IoPath.Combine(KnowledgeFiles.ExamplesFolder, "cities.json")),
            ("cities.xml", "c2x8m5ld", IoPath.Combine(KnowledgeFiles.ExamplesFolder, "cities.xml")),
            // The countries first, which keep the counterpart the regions name; then the regions, which take its name; then the countries again, which drop it.
            ("relations/countries.yaml", "c8r2n5vw", ""),
            ("relations/regions.yaml", "r4g7m1zx", IoPath.Combine(KnowledgeFiles.ShowcaseFolder, "relations", "regions.yaml")),
            ("relations/countries.yaml", "", IoPath.Combine(KnowledgeFiles.ShowcaseFolder, "relations", "countries.yaml")),
        ];

        foreach ((string file, string id, string shipped) in steps)
        {
            // Act.
            var path = IoPath.Combine(folder, file);
            var before = Pairs(KnowledgeDocumentStore.Read(path).Body!.Table);
            var body = KnowledgeDocumentStore.Read(path).Body!;
            (byte[]? bytes, string refusal) = body.Change(KnowledgeLinkConversion.Of(body.Table, path, TargetsOf(path), () => id));
            Assert.True(bytes is not null, refusal);
            File.WriteAllBytes(path, bytes);

            // Assert.
            Assert.Equal(before, Pairs(KnowledgeDocumentStore.Read(path).Body!.Table));
            if (shipped.Length > 0)
            {
                Assert.Equal(File.ReadAllBytes(shipped), bytes);
            }
        }

        // The showcase holds the module's examples as they are.
        foreach (var format in new[] { "yaml", "json", "xml" })
        {
            Assert.Equal(File.ReadAllBytes(IoPath.Combine(KnowledgeFiles.ExamplesFolder, "cities." + format)), File.ReadAllBytes(IoPath.Combine(KnowledgeFiles.ShowcaseFolder, format, "cities." + format)));
            Assert.Equal(File.ReadAllBytes(IoPath.Combine(KnowledgeFiles.ExamplesFolder, "provinces.yaml")), File.ReadAllBytes(IoPath.Combine(KnowledgeFiles.ShowcaseFolder, format, "provinces.yaml")));
        }
    }

    [Fact]
    public async Task AFileWithEntriesWithoutAnId_IsConverted_AndGivesThemTheirIds_InTheSameStep()
    {
        // Arrange: the view written without an id.
        await File.WriteAllTextAsync(Cities, (await File.ReadAllTextAsync(Cities, TestContext.Current.CancellationToken)).Replace("  - id: v1\n    name: Table\n", "  - name: Table\n", StringComparison.Ordinal).Replace("  - id: v1\r\n    name: Table\r\n", "  - name: Table\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await using var table = new EditingTable(Cities, rootPath: _testFolder.Path, newDocumentId: () => GivenId);

        // Act.
        var settled = await table.Edit(new TableGesture("setCell", RowId: "utr", ColumnId: "p1", Values: ["Utrecht (city)"]));

        // Assert: written, its version raised and its view given an id with it.
        Assert.True(settled.Written, settled.Error);
        var written = table.OnDisk();
        Assert.Equal(GivenId, written.DocumentId);
        Assert.Contains("ded: \"0.3\"", System.Text.Encoding.UTF8.GetString(table.Bytes()), StringComparison.Ordinal);
        Assert.Matches("^[0-9a-z]+$", Assert.Single(written.Views).Id);
    }

    [Fact]
    public void ARelationWhoseTargetHasNoIdYet_IsLeftAsItIs_AndReported()
    {
        // Arrange: the provinces as a file made before links too.
        File.WriteAllText(Provinces, File.ReadAllText(Provinces).Replace("id: k3f9x2ma\n", "", StringComparison.Ordinal).Replace("id: k3f9x2ma\r\n", "", StringComparison.Ordinal));
        var body = KnowledgeDocumentStore.Read(Cities).Body!;

        // Act.
        var changes = KnowledgeLinkConversion.Of(body.Table, Cities, TargetsOf(Cities), () => GivenId);
        var findings = KnowledgeValidator.Validate(body.Table, body.Model, TargetsOf(Cities));

        // Assert: the relation to the provinces is not touched, and is reported; the cities still get their id.
        Assert.DoesNotContain(changes, change => change is Specification.Fbl.Planning.ModelChange.Set { Id: "p2" });
        Assert.Contains(changes, change => change is Specification.Fbl.Planning.ModelChange.SetDocumentId { Id: GivenId });
        Assert.Contains(findings, finding => finding is { Code: KnowledgeValidator.UnresolvedTarget, ColumnId: "p2" });
    }
}
