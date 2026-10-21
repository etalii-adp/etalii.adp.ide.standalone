using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// A selected row in the property grid (knowledge-designer Requirement 4.6): the row is found
/// under its table's file and named by its title, its values are the grid's rows, each edited as
/// its type is, and an edit made there is the undoable step it is in the table.
/// </summary>
public sealed class KnowledgeRowContextTests : IDisposable
{
    private readonly FileWriter _writer = new();

    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-row-").FullName;
    private readonly KnowledgeDocuments _documents = new();
    private readonly HistoryStackStore _histories;
    private readonly KnowledgeRowSourceResolver _resolver;
    private readonly KnowledgeRowPropertyProvider _provider;

    public KnowledgeRowContextTests()
    {
        var services = new Handlers
        {
            [typeof(ICommandHandler<KnowledgeEditCommand>)] = new KnowledgeEditCommandHandler(_documents, new FileWriter()),
            [typeof(ICommandHandler<RestoreKnowledgeFilesCommand>)] = new RestoreKnowledgeFilesCommandHandler(_documents, new FileWriter()),
            [typeof(ICommandHandler<RestoreDocumentCommand<IKnowledgeDocumentStore>>)] = new RestoreDocumentCommandHandler<IKnowledgeDocumentStore>(_documents, new FileWriter()),
        };
        _histories = new HistoryStackStore(new CommandDispatcher(services));
        _resolver = new KnowledgeRowSourceResolver(_documents);
        _provider = new KnowledgeRowPropertyProvider(_histories);
        KnowledgeFiles.CopyExample("cities.yaml", _root);
        File.WriteAllText(Registration, "etalii/knowledge\r\nbody: cities.yaml\r\n");
    }

    public void Dispose()
    {
        _histories.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is the system's to clear.
        }
    }

    private string Cities => IoPath.Combine(_root, "cities.yaml");

    private string Registration => IoPath.Combine(_root, "cities.adp");

    /// <summary>The file's own level of a selection, as the explorer's resolver gives it.</summary>
    private ContextResolvedLevel File_(string path) => new(
        new ContextSource { EntryId = ShortGuid.NewShortGuid() },
        [IoPath.GetFileName(path)],
        ContextScope.Hierarchy,
        new ContextTarget(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid(), ProjectRoot.From(_root)),
        new ContextLevelDetail(),
        _resolver);

    private async Task<ContextLevelResolution> Resolve(string rowId, string? file = null) => await _resolver.ResolveAsync(
        ShortGuid.NewShortGuid(),
        ProjectRoot.From(_root),
        new ContextSource { ElementId = new ElementId { Value = rowId } },
        [],
        File_(file ?? Registration),
        TestContext.Current.CancellationToken);

    private async Task<ContextTarget> Row(string rowId) => Assert.IsType<ResolvedContextLevel>(await Resolve(rowId)).Level.Target;

    private async Task<Dictionary<string, ContextPropertyDefinition>> Described(string rowId) =>
        (await _provider.DescribeAsync(await Row(rowId), TestContext.Current.CancellationToken)).ToDictionary(row => row.Id);

    [Fact]
    public async Task ARow_IsSelectedUnderItsTablesFile_ByItsId_AndNamedByItsTitle()
    {
        // Act: under the registration, under the data file, and a row that is not there.
        var underRegistration = Assert.IsType<ResolvedContextLevel>(await Resolve("r1")).Level;
        var underBody = Assert.IsType<ResolvedContextLevel>(await Resolve("r1", Cities)).Level;
        var missing = await Resolve("r999");

        // Assert: the same row of the same file either way, marked as this module's.
        Assert.Equal((ContextScope.DiagramElement, Cities, "r1", KnowledgeRowSourceResolver.RowOrigin), (underRegistration.Target.Scope, underRegistration.Target.ResolvedFullPath, underRegistration.Target.ElementId, underRegistration.Target.Origin));
        Assert.Equal(underRegistration.Target.ResolvedFullPath, underBody.Target.ResolvedFullPath);
        Assert.Equal(["Amsterdam"], underRegistration.RelativePath);
        Assert.Equal("Amsterdam", underRegistration.Detail.Element.Text);
        Assert.IsType<RejectedContextLevel>(missing);

        // A file that is no knowledge table has no rows to select.
        var notes = IoPath.Combine(_root, "notes.yaml");
        await File.WriteAllTextAsync(notes, "just: notes\r\n", TestContext.Current.CancellationToken);
        Assert.IsType<RejectedContextLevel>(await Resolve("r1", notes));
    }

    [Fact]
    public async Task ARowsValues_AreTheGridsRows_EachAsItsTypeIsEdited()
    {
        // Act.
        var rows = await Described("r1");

        // Assert: one per property, in the words an author reads.
        Assert.Equal(("Name", "Amsterdam", ContextPropertyEditor.Line), (rows["p1"].Label, rows["p1"].Value, rows["p1"].Editor));
        Assert.Equal(("Netherlands", ContextPropertyEditor.Choice), (rows["p2"].Value, rows["p2"].Editor));
        Assert.Equal(["", "Netherlands", "Belgium"], rows["p2"].Choices.Take(3));
        Assert.Equal(("931298", ContextPropertyEditor.Line), (rows["p3"].Value, rows["p3"].Editor));
        Assert.Equal(("Port, Capital", ContextPropertyEditor.Tags), (rows["p4"].Value, rows["p4"].Editor));
        Assert.Equal(("true", ContextPropertyEditor.Toggle), (rows["p6"].Value, rows["p6"].Editor));
        Assert.Equal("1275-10-27", rows["p7"].Value);
        Assert.All(new[] { "p1", "p2", "p3", "p4", "p6", "p7" }, id => Assert.True(rows[id].IsEditable));

        // A relation is shown and not edited here.
        Assert.Equal((false, KnowledgeRowPropertyProvider.RelationStays), (rows["p5"].IsEditable, rows["p5"].ReadOnlyReason));

        // A selection that is not this module's is not answered for.
        var foreign = (await Row("r1")) with { Origin = new ToolOrigin("generic", "timeline") };
        Assert.Empty(await _provider.DescribeAsync(foreign, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEditInTheGrid_IsTheUndoableStepItIsInTheTable_AndWhatIsNotAValueIsRefused()
    {
        // Arrange.
        var row = await Row("r1");
        var before = await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken);

        // Act: a text, an option by its name, two tags by their names, and a checkbox.
        Assert.True((await Set("p1", "Amsterdam (NH)")).IsSuccess);
        Assert.True((await Set("p2", "Belgium")).IsSuccess);
        Assert.True((await Set("p4", "Capital")).IsSuccess);
        Assert.True((await Set("p6", "false")).IsSuccess);

        // Assert: in the file as ids, and in the grid as names.
        var cells = KnowledgeDocumentStore.Read(Cities).Body!.Table.Rows.Single(candidate => candidate.Id == "r1").Cells.ToDictionary(cell => cell.PropertyId, cell => cell.Values);
        Assert.Equal(["Amsterdam (NH)"], cells["p1"]);
        Assert.Equal(["o2"], cells["p2"]);
        Assert.Equal(["o4"], cells["p4"]);
        Assert.DoesNotContain("p6", cells.Keys);
        var rows = await Described("r1");
        Assert.Equal(("Amsterdam (NH)", "Belgium", "Capital", "false"), (rows["p1"].Value, rows["p2"].Value, rows["p4"].Value, rows["p6"].Value));

        // Act: what is no value of its property, and a relation.
        var bytes = await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken);
        var word = await Set("p3", "a great many");
        var unknown = await Set("p2", "Atlantis");
        var relation = await Set("p5", "zh");

        // Assert: each refused with its reason, and nothing written.
        Assert.Equal(KnowledgeNumberType.Expected, word.Error);
        Assert.Equal("'Atlantis' is not one of the options of Country.", unknown.Error);
        Assert.Equal(KnowledgeRowPropertyProvider.RelationStays, relation.Error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken));

        // Act: four undos.
        for (var step = 0; step < 4; step++)
        {
            Assert.True((await _histories.Get(ProjectRoot.From(_root)).UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }

        // Assert: the file as it was.
        Assert.Equal(before, await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken));
        return;

        async Task<ContextPropertyResult> Set(string propertyId, string value) => await _provider.SetAsync(row, propertyId, value, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ASelectedRow_IsFollowed_WhileItsTableChanges_AndLetGoWhenItIsDeleted()
    {
        // Arrange: the row is tracked, as a selection is.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("r1")).Level;
        List<IReadOnlyList<string>?> told = [];
        using var tracking = _resolver.Track(ShortGuid.NewShortGuid(), ProjectRoot.From(_root), level, told.Add);

        // Act: renamed through the grid, then deleted.
        await _provider.SetAsync(level.Target, "p1", "Mokum", TestContext.Current.CancellationToken);
        var body = KnowledgeDocumentStore.Read(Cities).Body!;
        (byte[]? without, _) = body.Change([new ModelChange.Remove("r1")]);
        await _writer.SaveAsync(Cities, without!, TestContext.Current.CancellationToken);
        await _documents.ReloadAsync(Cities, TestContext.Current.CancellationToken);

        // Assert: told its new title, and then that it is gone.
        Assert.Equal(2, told.Count);
        Assert.Equal(["Mokum"], told[0]);
        Assert.Null(told[1]);
    }

    private sealed class Handlers : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
