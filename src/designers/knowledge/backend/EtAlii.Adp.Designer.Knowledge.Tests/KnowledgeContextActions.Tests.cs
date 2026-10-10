using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Xunit;
using ContextScope = EtAlii.Adp.Documents.Wire.ContextScope;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// Adding a relation from the explorer (knowledge-designer Requirement 5.1): the action is offered
/// on a knowledge file and on nothing else, asks for the table to relate to in the file dialog -
/// which accepts knowledge files only and offers this table first - and makes the relation as one
/// undoable step.
/// </summary>
public sealed class KnowledgeContextActionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-knowledge-actions-").FullName;
    private readonly KnowledgeDocuments _documents = new();
    private readonly HistoryStackStore _histories;
    private readonly KnowledgeContextActionProvider _provider;

    public KnowledgeContextActionsTests()
    {
        var services = new Handlers
        {
            [typeof(ICommandHandler<KnowledgeEditCommand>)] = new KnowledgeEditCommandHandler(_documents),
            [typeof(ICommandHandler<RestoreKnowledgeFilesCommand>)] = new RestoreKnowledgeFilesCommandHandler(_documents),
            [typeof(ICommandHandler<RestoreDocumentCommand<IKnowledgeDocumentStore>>)] = new RestoreDocumentCommandHandler<IKnowledgeDocumentStore>(_documents),
        };
        _histories = new HistoryStackStore(new CommandDispatcher(services));
        _provider = new KnowledgeContextActionProvider(_histories);
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

    private string Provinces => IoPath.Combine(_root, KnowledgeFiles.Related);

    /// <summary>The example and the table it relates to, each with the registration that makes it a document of the project.</summary>
    private string Registered()
    {
        KnowledgeFiles.CopyExample("cities.yaml", _root);
        File.WriteAllText(IoPath.Combine(_root, "cities.adp"), "etalii/knowledge\r\nbody: cities.yaml\r\n");
        File.WriteAllText(IoPath.Combine(_root, "provinces.adp"), "etalii/knowledge\r\n");
        File.WriteAllText(IoPath.Combine(_root, "notes.yaml"), "just: notes\r\n");
        File.WriteAllText(IoPath.Combine(_root, "diagram.adp"), "generic/timeline\r\n");
        return IoPath.Combine(_root, "cities.adp");
    }

    private ContextTarget Target(string path, bool container = false) => new(ContextScope.Hierarchy, path, container, ShortGuid.NewShortGuid(), _root);

    private static List<ContextActionDefinition> Leaves(IEnumerable<ContextActionGroupDefinition> groups) =>
        [.. groups.SelectMany(group => group.Actions).SelectMany(action => action.Children is { Count: > 0 } children ? Leaves(children) : [action])];

    [Fact]
    public async Task AKnowledgeFile_OffersARelation_FourWays_FromItsRegistrationAndFromItsDataFile()
    {
        // Arrange.
        var registration = Registered();

        // Act.
        var fromRegistration = await _provider.DiscoverAsync(Target(registration), TestContext.Current.CancellationToken);
        var fromData = await _provider.DiscoverAsync(Target(Cities), TestContext.Current.CancellationToken);
        var byName = await _provider.DiscoverAsync(Target(IoPath.Combine(_root, "provinces.adp")), TestContext.Current.CancellationToken);

        // Assert: one entry, with the four ways under it.
        string[] ways = [KnowledgeContextActionProvider.ManyOneWay, KnowledgeContextActionProvider.OneOneWay, KnowledgeContextActionProvider.ManyTwoWay, KnowledgeContextActionProvider.OneTwoWay];
        Assert.Equal("Add relation", Assert.Single(Assert.Single(fromRegistration).Actions).Label);
        Assert.Equal(ways, Leaves(fromRegistration).Select(action => action.Id));
        Assert.Equal(ways, Leaves(fromData).Select(action => action.Id));

        // A registration that names no file is the registration of the file of its own name.
        Assert.Equal(ways, Leaves(byName).Select(action => action.Id));
    }

    [Fact]
    public async Task NothingElse_OffersARelation()
    {
        // Arrange.
        Registered();

        // Act.
        var folder = await _provider.DiscoverAsync(Target(_root, container: true), TestContext.Current.CancellationToken);
        var other = await _provider.DiscoverAsync(Target(IoPath.Combine(_root, "notes.yaml")), TestContext.Current.CancellationToken);
        var diagram = await _provider.DiscoverAsync(Target(IoPath.Combine(_root, "diagram.adp")), TestContext.Current.CancellationToken);
        var gone = await _provider.DiscoverAsync(Target(IoPath.Combine(_root, "gone.adp")), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(folder);
        Assert.Empty(other);
        Assert.Empty(diagram);
        Assert.Empty(gone);
    }

    [Fact]
    public async Task TheTableToRelateTo_IsAskedForInTheFileDialog_WhichTakesKnowledgeFilesOnly_ThisOneFirst()
    {
        // Arrange.
        var registration = Registered();

        // Act.
        var asked = await _provider.ExecuteAsync(Target(registration), KnowledgeContextActionProvider.ManyOneWay, TestContext.Current.CancellationToken);

        // Assert.
        var request = Assert.IsType<ContextExecutionRequiresFile>(asked).Request;
        Assert.Equal("Relate to", request.Title);
        var pinned = Assert.Single(request.Pinned);
        Assert.Equal(("This table", Cities), (pinned.Label, pinned.FullPath));
        Assert.True(request.Accepts(Provinces));
        Assert.True(request.Accepts(Cities));
        Assert.False(request.Accepts(IoPath.Combine(_root, "notes.yaml")));
        Assert.False(request.Accepts(registration));

        // And what is chosen is judged by the same rule.
        // And what is chosen is judged by the same rule - named as the dialog names it, by its path from the project's root.
        Assert.True((await _provider.ValidateAsync(Target(registration), KnowledgeContextActionProvider.ManyOneWay, KnowledgeFiles.Related, TestContext.Current.CancellationToken)).Valid);
        Assert.False((await _provider.ValidateAsync(Target(registration), KnowledgeContextActionProvider.ManyOneWay, "notes.yaml", TestContext.Current.CancellationToken)).Valid);
    }

    [Theory]
    [InlineData(KnowledgeContextActionProvider.ManyOneWay, "none", false)]
    [InlineData(KnowledgeContextActionProvider.OneOneWay, "one", false)]
    [InlineData(KnowledgeContextActionProvider.ManyTwoWay, "none", true)]
    [InlineData(KnowledgeContextActionProvider.OneTwoWay, "one", true)]
    public async Task TheRelation_IsMadeAsItsEntrySays_InOneUndoableStep(string actionId, string limit, bool twoWay)
    {
        // Arrange.
        var registration = Registered();
        (byte[] ours, byte[] theirs) = (await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        var committed = await _provider.CommitAsync(Target(registration), actionId, KnowledgeFiles.Related, "", TestContext.Current.CancellationToken);

        // Assert: a relation to the other table under its name, holding what the entry said.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.Name == "Provinces");
        Assert.Equal(("relation", KnowledgeFiles.Related, limit), (relation.ValueType, relation.TargetFile, relation.Limit));

        // Two-way, the other table shows it under this table's name; one-way, the other table is as it was.
        var others = KnowledgeDocumentStore.Read(Provinces).Body!.Table.Properties;
        if (twoWay)
        {
            var other = others.Single(property => property.Name == "Cities");
            Assert.Equal((relation.Id, true, "cities.yaml"), (other.Counterpart, other.IsComputed, other.TargetFile));
            Assert.Equal(other.Id, relation.Counterpart);
        }
        else
        {
            Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
            Assert.Equal("", relation.Counterpart);
        }

        // Act: the application's undo.
        var undone = await _histories.Get(_root).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess);
        Assert.Equal(ours, await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken));
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARelationToTheTableItself_IsMadeByChoosingThisTable()
    {
        // Arrange.
        var registration = Registered();

        // Act.
        var committed = await _provider.CommitAsync(Target(registration), KnowledgeContextActionProvider.OneOneWay, "cities.yaml", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.Name == "Relation");
        Assert.Equal((KnowledgeRelations.Self, "one"), (relation.TargetFile, relation.Limit));
    }

    /// <summary>
    /// Found in the browser: the dialog names the chosen file by its path from the project's root, and
    /// that was read from the folder the server was started in, where no such file is.
    /// </summary>
    [Fact]
    public async Task TheChosenFile_IsNamedFromTheProjectsRoot_AlsoFromAFolderBelowIt()
    {
        // Arrange: the table in a folder of the project, and the one it is to relate to in another.
        Directory.CreateDirectory(IoPath.Combine(_root, "here"));
        Directory.CreateDirectory(IoPath.Combine(_root, "there"));
        KnowledgeFiles.CopyExample("cities.yaml", IoPath.Combine(_root, "here"));
        File.Copy(IoPath.Combine(_root, "here", KnowledgeFiles.Related), IoPath.Combine(_root, "there", "regions.yaml"));
        var table = IoPath.Combine(_root, "here", "cities.yaml");

        // Act: chosen as the dialog hands it over.
        var committed = await _provider.CommitAsync(Target(table), KnowledgeContextActionProvider.ManyOneWay, "there/regions.yaml", "", TestContext.Current.CancellationToken);

        // Assert: related, and named in the file by its path from the file itself.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(table).Body!.Table.Properties.Single(property => property.Name == "Provinces 2" || property.Name == "Provinces");
        Assert.Equal("../there/regions.yaml", relation.TargetFile);
    }

    [Fact]
    public async Task AnOpenTable_ShowsARelationMadeFromTheExplorer_AtOnce()
    {
        // Arrange: the table is open in a tab.
        var registration = Registered();
        await using var session = new KnowledgeSession(Cities, KnowledgeDocumentStore.Read, command => _histories.Get(_root).ExecuteAsync(command), _documents);

        // Act.
        await _provider.CommitAsync(Target(registration), KnowledgeContextActionProvider.ManyOneWay, KnowledgeFiles.Related, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(session.Baseline().Columns, column => column is { Name: "Provinces", Kind: "relation" });
    }

    /// <summary>The handlers by the type the dispatcher asks for them under.</summary>
    private sealed class Handlers : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
