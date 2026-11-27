using EtAlii.Adp.Context;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
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
    private readonly TestFolder _testFolder = TestFolder.Create("adp-knowledge-actions");
    private readonly string _root;
    private readonly KnowledgeDocuments _documents = new();
    private readonly HistoryStackStore _histories;
    private readonly KnowledgeContextActionProvider _provider;

    public KnowledgeContextActionsTests()
    {
        _root = _testFolder.Path;
        var services = new Handlers
        {
            [typeof(ICommandHandler<KnowledgeEditCommand>)] = new KnowledgeEditCommandHandler(_documents, new FileWriter()),
            [typeof(ICommandHandler<RestoreKnowledgeFilesCommand>)] = new RestoreKnowledgeFilesCommandHandler(_documents, new FileWriter()),
            [typeof(ICommandHandler<RestoreDocumentCommand<IKnowledgeDocumentStore>>)] = new RestoreDocumentCommandHandler<IKnowledgeDocumentStore>(_documents, new FileWriter()),
        };
        _histories = new HistoryStackStore(new CommandDispatcher(services));
        _provider = new KnowledgeContextActionProvider(_histories);
    }

    public void Dispose()
    {
        _histories.Dispose();
        _testFolder.Dispose();
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

    private ContextTarget Target(string path, bool container = false) => new(ContextScope.Hierarchy, path, container, ShortGuid.NewShortGuid(), ProjectRoot.From(_root));

    /// <summary>Adds a relation as its author does: the table, how many rows, and where it is shown - three answers, the last of which makes it.</summary>
    private async Task<ContextCommitResult> Related(ContextTarget target, string file, string holds, bool twoWay, string name = "")
    {
        var first = await _provider.CommitAsync(target, KnowledgeContextActionProvider.Relate, file, "", TestContext.Current.CancellationToken);
        if (first.Next is null)
        {
            return first;
        }

        var second = await _provider.CommitAsync(target, first.NextActionId, holds, "", TestContext.Current.CancellationToken);
        return second.Next is null
            ? second
            : await _provider.CommitAsync(target, second.NextActionId, twoWay ? KnowledgeContextActionProvider.TwoWay : KnowledgeContextActionProvider.OneWay, name, TestContext.Current.CancellationToken);
    }

    private static List<ContextActionDefinition> Leaves(IEnumerable<ContextActionGroupDefinition> groups) =>
        [.. groups.Actions().SelectMany(action => action.Children is { Count: > 0 } children ? Leaves(children) : [action])];

    [Fact]
    public async Task AKnowledgeFile_OffersARelation_AsOneEntry_FromItsRegistrationAndFromItsDataFile()
    {
        // Arrange.
        var registration = Registered();

        // Act.
        var fromRegistration = await _provider.DiscoverAsync(Target(registration), TestContext.Current.CancellationToken);
        var fromData = await _provider.DiscoverAsync(Target(Cities), TestContext.Current.CancellationToken);
        var byName = await _provider.DiscoverAsync(Target(IoPath.Combine(_root, "provinces.adp")), TestContext.Current.CancellationToken);

        // Assert: one entry, which asks what it needs to know after it is chosen.
        string[] ways = [KnowledgeContextActionProvider.Relate];
        Assert.Equal("Add relation…", Assert.Single(Assert.Single(fromRegistration).Actions).Label);
        Assert.Equal(ways, Leaves(fromRegistration).Select(action => action.Id));
        Assert.Equal(ways, Leaves(fromData).Select(action => action.Id));

        // A registration that names no file is the registration of the file of its own name.
        Assert.Equal(ways, Leaves(byName).Select(action => action.Id));
    }

    [Fact]
    public async Task TwoKnowledgeFilesWithNoRegistration_AreRelated_AndHeldByTheIndexOfLinks()
    {
        // Arrange: the example and the table it relates to, copied in with no registration beside either (spec 093 US2).
        KnowledgeFiles.CopyExample("cities.yaml", _root);

        // Act: a relation offered from the bare file, to the other bare file.
        var offered = await _provider.DiscoverAsync(Target(Cities), TestContext.Current.CancellationToken);
        var asked = await _provider.ExecuteAsync(Target(Cities), KnowledgeContextActionProvider.Relate, TestContext.Current.CancellationToken);
        var made = await Related(Target(Cities), KnowledgeFiles.Related, KnowledgeContextActionProvider.Many, twoWay: false);
        var indexed = new KnowledgeLinkIndexer().Index(Provinces, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Assert: offered, the other table among the targets, the relation made; and the target held by its id and rows.
        Assert.Equal([KnowledgeContextActionProvider.Relate], Leaves(offered).Select(action => action.Id));
        Assert.True(Assert.IsType<ContextExecutionRequiresFile>(asked).Request.Accepts(Provinces));
        Assert.True(made.Completed, made.Error);
        Assert.Equal(KnowledgeFiles.RelatedLink, KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.Name == "Provinces").TargetFile);
        Assert.NotNull(indexed);
        Assert.Contains(indexed.Elements, element => element.Id == "nh");
        Assert.Empty(Directory.EnumerateFiles(_root, "*.adp"));
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
        var asked = await _provider.ExecuteAsync(Target(registration), KnowledgeContextActionProvider.Relate, TestContext.Current.CancellationToken);

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
        Assert.True((await _provider.ValidateAsync(Target(registration), KnowledgeContextActionProvider.Relate, KnowledgeFiles.Related, TestContext.Current.CancellationToken)).Valid);
        Assert.False((await _provider.ValidateAsync(Target(registration), KnowledgeContextActionProvider.Relate, "notes.yaml", TestContext.Current.CancellationToken)).Valid);
    }

    [Fact]
    public async Task ADiagramOfTheWorkspace_IsOfferedToRelateTo_AndTheRelationToIt_IsOneWay_WithItsLimitInElements()
    {
        // Arrange: a timeline the workspace's index holds, beside the tables (etalii.adp spec 092, FR-025, T035).
        var registration = Registered();
        var plan = IoPath.Combine(_root, "plan.tml");
        await File.WriteAllTextAsync(plan, "timeline: 1\nid: t7m2k9qa\nelements:\n  - id: launch\n    label: Launch\n", TestContext.Current.CancellationToken);
        var workspace = new LinkWorkspace(_root.Replace('\\', '/'), [
            new IndexedDocument(plan.Replace('\\', '/'), "t7m2k9qa", "label", [new IndexedElement("launch", [new KeyValuePair<string, string>("label", "Launch")])], []) { Origin = "generic/timeline" },
        ]);
        var provider = new KnowledgeContextActionProvider(_histories, new FixedLinkIndex(workspace));

        // Act: the dialog, the timeline chosen, and one element a cell.
        var asked = Assert.IsType<ContextExecutionRequiresFile>(await provider.ExecuteAsync(Target(registration), KnowledgeContextActionProvider.Relate, TestContext.Current.CancellationToken)).Request;
        var chosen = await provider.CommitAsync(Target(registration), KnowledgeContextActionProvider.Relate, "plan.tml", "", TestContext.Current.CancellationToken);
        var limit = Assert.IsType<ContextExecutionRequiresChoice>(chosen.Next).Request;
        var made = await provider.CommitAsync(Target(registration), chosen.NextActionId, KnowledgeContextActionProvider.One, "", TestContext.Current.CancellationToken);

        // Assert: offered after the tables; the limit in elements, and the relation made at once - there is no other side to ask about.
        Assert.True(asked.Accepts(plan));
        Assert.True(asked.Rank!(plan) > asked.Rank!(Provinces));
        Assert.Equal(["Any number of elements", "One element"], limit.Options.Select(option => option.Label));
        Assert.Equal("Add relation", limit.ConfirmLabel);
        Assert.True(made.Completed, made.Error);
        Assert.Null(made.Next);
        var relation = KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.TargetFile == "$t7m2k9qa@plan.tml");
        Assert.Equal(("one", ""), (relation.Limit, relation.BacklinkName));
    }

    [Fact]
    public async Task WhereTheIndexTracksNoWorkspace_TheDialogOffersTheTablesAlone_AndFailsOnNothing()
    {
        // Arrange: an index that holds no workspace for this table's folder, as before the folder is tracked.
        var registration = Registered();
        var plan = IoPath.Combine(_root, "plan.tml");
        await File.WriteAllTextAsync(plan, "timeline: 1\nid: t7m2k9qa\nelements: []\n", TestContext.Current.CancellationToken);
        var provider = new KnowledgeContextActionProvider(_histories, new FixedLinkIndex(null!));

        // Act.
        var asked = Assert.IsType<ContextExecutionRequiresFile>(await provider.ExecuteAsync(Target(registration), KnowledgeContextActionProvider.Relate, TestContext.Current.CancellationToken)).Request;

        // Assert: the tables, and no diagram, since none is known.
        Assert.True(asked.Accepts(Provinces));
        Assert.False(asked.Accepts(plan));
    }

    [Theory]
    [InlineData(KnowledgeContextActionProvider.Many, "none", false)]
    [InlineData(KnowledgeContextActionProvider.One, "one", false)]
    [InlineData(KnowledgeContextActionProvider.Many, "none", true)]
    [InlineData(KnowledgeContextActionProvider.One, "one", true)]
    public async Task TheRelation_IsMadeAsItsAnswersSay_InOneUndoableStep(string holds, string limit, bool twoWay)
    {
        // Arrange.
        var registration = Registered();
        (byte[] ours, byte[] theirs) = (await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act.
        var committed = await Related(Target(registration), KnowledgeFiles.Related, holds, twoWay);

        // Assert: a relation to the other table under its name, holding what was answered.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.Name == "Provinces");
        Assert.Equal(("relation", KnowledgeFiles.RelatedLink, limit), (relation.ValueType, relation.TargetFile, relation.Limit));

        // Two-way, the other table shows its backlinks under this table's name; either way, the other table is as it was.
        Assert.Equal((twoWay ? "Cities" : "", ""), (relation.BacklinkName, relation.Counterpart));
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: the application's undo.
        var undone = await _histories.Get(ProjectRoot.From(_root)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess);
        Assert.Equal(ours, await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken));
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARelation_IsAskedForInNotionsOrder_TheTable_HowManyRows_AndWhetherBothTablesShowIt()
    {
        // Arrange.
        var registration = Registered();
        (byte[] ours, byte[] theirs) = (await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // Act: the table is answered.
        var first = await _provider.CommitAsync(Target(registration), KnowledgeContextActionProvider.Relate, KnowledgeFiles.Related, "", TestContext.Current.CancellationToken);

        // Assert: taken, and followed by how many rows a cell holds; nothing is written yet.
        Assert.True(first.Completed, first.Error);
        var holds = Assert.IsType<ContextExecutionRequiresChoice>(first.Next).Request;
        Assert.Equal([KnowledgeContextActionProvider.Many, KnowledgeContextActionProvider.One], holds.Options.Select(option => option.Id));
        Assert.Null(holds.NameField);
        Assert.Equal(ours, await File.ReadAllBytesAsync(Cities, TestContext.Current.CancellationToken));

        // Act: one row is answered.
        var second = await _provider.CommitAsync(Target(registration), first.NextActionId, KnowledgeContextActionProvider.One, "", TestContext.Current.CancellationToken);

        // Assert: followed by where it is shown, with a name to give for the other table only when it is shown there.
        var shown = Assert.IsType<ContextExecutionRequiresChoice>(second.Next).Request;
        Assert.Equal([KnowledgeContextActionProvider.OneWay, KnowledgeContextActionProvider.TwoWay], shown.Options.Select(option => option.Id));
        Assert.Equal(new ContextTextFieldRequest(KnowledgeContextActionProvider.OtherSideLabel, "Cities"), shown.NameField);
        Assert.NotEqual("", shown.Options[0].NameSuppressedReason);
        Assert.Equal(("", "Cities"), (shown.Options[1].NameSuppressedReason, shown.Options[1].SuggestedValue));
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // What is typed there is judged as a name.
        Assert.True((await _provider.ValidateAsync(Target(registration), second.NextActionId, "Seats", TestContext.Current.CancellationToken)).Valid);
        var empty = await _provider.ValidateAsync(Target(registration), second.NextActionId, "  ", TestContext.Current.CancellationToken);
        Assert.Equal((false, KnowledgeContextActionProvider.OtherSideNeedsName), (empty.Valid, empty.Reason));

        // Act: both tables, under a name.
        var last = await _provider.CommitAsync(Target(registration), second.NextActionId, KnowledgeContextActionProvider.TwoWay, " Seats ", TestContext.Current.CancellationToken);

        // Assert: made, with nothing more to ask: one row per cell here, shown in the other table under that name, which is not written there.
        Assert.True(last.Completed, last.Error);
        Assert.Null(last.Next);
        Assert.Contains(KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties, property => property is { ValueType: "relation", Limit: "one", TargetFile: KnowledgeFiles.RelatedLink, BacklinkName: "Seats", Counterpart: "" });
        Assert.Equal(theirs, await File.ReadAllBytesAsync(Provinces, TestContext.Current.CancellationToken));

        // An answer that is none of a step's is refused.
        var odd = await _provider.CommitAsync(Target(registration), first.NextActionId, "several", "", TestContext.Current.CancellationToken);
        Assert.False(odd.Completed);
    }

    [Fact]
    public async Task ARelationToTheTableItself_IsMadeByChoosingThisTable()
    {
        // Arrange.
        var registration = Registered();

        // Act.
        var committed = await Related(Target(registration), "cities.yaml", KnowledgeContextActionProvider.One, twoWay: false);

        // Assert.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(Cities).Body!.Table.Properties.Single(property => property.Name == "Relation");
        Assert.Equal((KnowledgeFiles.SelfLink(".yaml"), "one"), (relation.TargetFile, relation.Limit));
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
        var committed = await Related(Target(table), "there/regions.yaml", KnowledgeContextActionProvider.Many, twoWay: false);

        // Assert: related, and named in the file by a link to the chosen file's document under its file name.
        Assert.True(committed.Completed, committed.Error);
        var relation = KnowledgeDocumentStore.Read(table).Body!.Table.Properties.Single(property => property.Name == "Provinces 2" || property.Name == "Provinces");
        Assert.Equal("$p7v3k9qd@regions.yaml", relation.TargetFile);
    }

    [Fact]
    public async Task AnOpenTable_ShowsARelationMadeFromTheExplorer_AtOnce()
    {
        // Arrange: the table is open in a tab.
        var registration = Registered();
        await using var session = new KnowledgeSession(Cities, KnowledgeDocumentStore.Read, command => _histories.Get(ProjectRoot.From(_root)).ExecuteAsync(command), _documents);

        // Act.
        await Related(Target(registration), KnowledgeFiles.Related, KnowledgeContextActionProvider.Many, twoWay: false);

        // Assert: without reopening. The open table reads its file again through its watched file,
        // one re-read at a time and off the writer's thread, so it is shown just after the write
        // rather than during it.
        Assert.True(
            await Eventually.TrueAsync(() => session.Baseline().Columns.Any(column => column is { Name: "Provinces", Kind: "relation" }), TestContext.Current.CancellationToken, TimeSpan.FromSeconds(10)),
            "The open table never showed the relation.");
    }

    /// <summary>The handlers by the type the dispatcher asks for them under.</summary>
    private sealed class Handlers : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
