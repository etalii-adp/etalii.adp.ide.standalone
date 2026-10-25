using EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// Two elements of one kind with one key each have an id of their own (finding B9, Requirement
/// 6.4, amendment 1 as ruled on 2026-10-11 and its seven choices as ruled on 2026-10-12).
/// </summary>
/// <remarks>
/// Every test reads the ids the store hands out, in document order: the identities list one
/// entry per element, components first, in the order the document declares them.
/// </remarks>
public sealed class WardleyDuplicateKeysTests : IDisposable
{
    private const string TwoTeas = "component Tea [0.5, 0.5]\ncomponent Tea [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\n";

    private readonly TestFolder _testFolder = TestFolder.Create("wardley-duplicate-keys");
    private readonly List<ServiceProvider> _providers = [];
    private readonly string _path;
    private ServiceProvider _services;

    public WardleyDuplicateKeysTests()
    {
        _path = IoPath.Combine(_testFolder.Path, "map.owm");
        _services = Provider();
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _testFolder.Dispose();
    }

    private IWardleyDocumentStore Documents => _services.GetRequiredService<IWardleyDocumentStore>();

    private IHistoryStack History => _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));

    private ServiceProvider Provider()
    {
        var provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    /// <summary>Opens the map again, as a restart of the application does.</summary>
    private void Reopen() => _services = Provider();

    private void Write(string text) => File.WriteAllText(_path, text);

    private string Read() => File.ReadAllText(_path);

    private async Task<string[]> ComponentIdsAsync()
    {
        await Documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return Documents.Identities(_path)
            .Where(entry => entry.Kind == WardleyIdentityKind.Component)
            .Select(entry => entry.Id)
            .ToArray();
    }

    private async Task<CommandResult> ExecuteAsync(ICommand command)
    {
        await Documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return await History.ExecuteAsync(command, TestContext.Current.CancellationToken);
    }

    private Task UndoAsync() => History.UndoAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task TwoHolders_HaveTwoIds_AndBothAreTheSameAfterASaveAndAReopen()
    {
        // Arrange.
        Write(TwoTeas);
        var opened = await ComponentIdsAsync();

        // Act: an edit that writes the map, then a fresh application on the same folder.
        var added = await ExecuteAsync(new AddWardleyElementCommand(_path, "component", "Kettle", 0.4d, 0.4d));
        var saved = await ComponentIdsAsync();
        Reopen();
        var reopened = await ComponentIdsAsync();

        // Assert.
        Assert.True(added.IsSuccess, added.Error);
        Assert.NotEqual(opened[0], opened[1]);
        Assert.Equal(opened, saved[..3]);
        Assert.Equal(saved, reopened);
    }

    [Fact]
    public async Task AnIdentitiesFileAsTodaysCodeWritesIt_GivesTheFirstHolderItsStoredId()
    {
        // Arrange: today's code writes one entry per holder, all with the first holder's key and id.
        Write(TwoTeas);
        await File.WriteAllTextAsync(
            WardleyIdentities.PathFor(_path),
            """
            [
              { "id": "stored1", "kind": "component", "key": "Tea" },
              { "id": "stored1", "kind": "component", "key": "Tea" },
              { "id": "stored2", "kind": "component", "key": "Cup" }
            ]
            """,
            TestContext.Current.CancellationToken);

        // Act.
        var ids = await ComponentIdsAsync();

        // Assert: the first holder keeps the stored id, the second has one of its own.
        Assert.Equal("stored1", ids[0]);
        Assert.NotEqual("stored1", ids[1]);
        Assert.Equal("stored2", ids[2]);
    }

    [Fact]
    public async Task ALaterHoldersEntry_IsReadBackToItsHolder_AndNoKeyOfTheCorpusEndsWithWhiteSpace()
    {
        // Arrange: the key of a later holder is the first holder's, U+001F, the ordinal and a tab.
        Write(TwoTeas);
        await File.WriteAllTextAsync(
            WardleyIdentities.PathFor(_path),
            """
            [
              { "id": "first", "kind": "component", "key": "Tea" },
              { "id": "second", "kind": "component", "key": "Tea\u001F2\t" },
              { "id": "cup", "kind": "component", "key": "Cup" }
            ]
            """,
            TestContext.Current.CancellationToken);

        // Act.
        var ids = await ComponentIdsAsync();
        var keys = Corpus()
            .SelectMany(text => WardleyIdentities.Reconcile(WardleyParser.Parse(LineDocument.Parse(text)), []))
            .Where(entry => !entry.Key.EndsWith('\t'))
            .Select(entry => entry.Key)
            .ToArray();

        // Assert. A key that ends with a tab is a later holder's and nothing else's, so the key of
        // no element may end with white space.
        Assert.Equal(["first", "second", "cup"], ids);
        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => key.Length > 0 && char.IsWhiteSpace(key[^1]));
    }

    [Fact]
    public async Task ASelectionAMoveAndACommand_ReachTheHolderTheirIdNames()
    {
        // Arrange.
        Write(TwoTeas);
        var ids = await ComponentIdsAsync();
        var map = Documents.Get(_path)!.Model;

        // Act.
        var subject = WardleySubject.Of(_path, map, Documents, ids[1]);
        var moved = await ExecuteAsync(new MoveWardleyElementCommand(_path, ids[1], 0.1d, 0.9d));
        var marked = await ExecuteAsync(new SetWardleyInertiaCommand(_path, ids[1], Inertia: true));

        // Assert.
        Assert.Equal(2u, subject.Component!.Line);
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.True(marked.IsSuccess, marked.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Tea [0.1, 0.9] inertia\ncomponent Cup [0.8, 0.2]\n", Read());
    }

    [Fact]
    public async Task ALinkWrittenAsARepeatedName_HasTheFirstHolderAsItsEnd()
    {
        // Arrange: written once, so that the ids are those a save keeps.
        Write(TwoTeas + "Tea->Cup\n");
        await ExecuteAsync(new AddWardleyElementCommand(_path, "component", "Kettle", 0.4d, 0.4d));
        var ids = await ComponentIdsAsync();
        var map = Documents.Get(_path)!.Model;
        var mapper = _services.GetRequiredService<WardleyElementMapper>();

        // Act.
        var link = mapper.Elements(map, Documents.Identities(_path))
            .Single(element => element.Type == WardleyElementTypes.Link);
        var payload = WardleyLinkPayload.Parser.ParseFrom(link.Payload.Span);

        // Assert.
        Assert.NotEqual(ids[0], ids[1]);
        Assert.Equal(ids[0], payload.SourceId);
        Assert.Equal(ids[2], payload.TargetId);
    }

    [Fact]
    public async Task ALinkDrawnFromALaterHolder_RenamesItWithANumber_AttachesToIt_AndKeepsItsId_InOneUndoStep()
    {
        // Arrange.
        Write(TwoTeas);
        var ids = await ComponentIdsAsync();

        // Act.
        var linked = await ExecuteAsync(new SetWardleyLinkCommand(_path, ids[1], ids[2], WardleyLinkKind.Dependency, Present: true));
        var after = await ComponentIdsAsync();
        var written = Read();
        await UndoAsync();
        var undone = await ComponentIdsAsync();

        // Assert.
        Assert.True(linked.IsSuccess, linked.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Tea 2 [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\nTea 2->Cup\n", written);
        Assert.Equal(ids, after);
        Assert.Equal(TwoTeas, Read());
        Assert.Equal(ids, undone);
    }

    [Fact]
    public async Task AnEvolutionDrawnFromALaterHolder_RenamesItWithANumber()
    {
        // Arrange.
        Write(TwoTeas);
        var ids = await ComponentIdsAsync();

        // Act.
        var evolved = await ExecuteAsync(new SetWardleyEvolveCommand(_path, ids[1], Present: true, 0.9d));
        var after = await ComponentIdsAsync();

        // Assert.
        Assert.True(evolved.IsSuccess, evolved.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Tea 2 [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\nevolve Tea 2 0.9\n", Read());
        Assert.Equal(ids, after);
    }

    [Fact]
    public async Task APipelineDrawnFromALaterHolder_RenamesItWithANumber()
    {
        // Arrange.
        Write(TwoTeas);
        var ids = await ComponentIdsAsync();

        // Act.
        var started = await ExecuteAsync(new SetWardleyPipelineMembershipCommand(_path, ids[1], "Pot", Member: true, 0.6d));
        var after = await ComponentIdsAsync();
        var lines = Read().Split('\n');

        // Assert.
        Assert.True(started.IsSuccess, started.Error);
        Assert.Equal("component Tea 2 [0.3, 0.7]", lines[1]);
        Assert.Equal("pipeline Tea 2", lines[3]);
        Assert.Equal(ids, after);
    }

    [Fact]
    public async Task WhereTheNameWithTwoIsTaken_TheNumberIsThree()
    {
        // Arrange.
        const string text = "component Tea [0.5, 0.5]\ncomponent Tea [0.3, 0.7]\ncomponent Tea 2 [0.8, 0.2]\n";
        Write(text);
        var ids = await ComponentIdsAsync();

        // Act.
        var linked = await ExecuteAsync(new SetWardleyLinkCommand(_path, ids[2], ids[1], WardleyLinkKind.Dependency, Present: true));

        // Assert.
        Assert.True(linked.IsSuccess, linked.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Tea 3 [0.3, 0.7]\ncomponent Tea 2 [0.8, 0.2]\nTea 2->Tea 3\n", Read());
    }

    [Fact]
    public async Task RenamingTheFirstHolder_TakesItsStatementsAlong_AndLeavesTheOthersTheirNamesAndIds()
    {
        // Arrange.
        Write(TwoTeas + "Tea->Cup\nevolve Tea 0.9\n");
        var ids = await ComponentIdsAsync();

        // Act.
        var renamed = await ExecuteAsync(new RenameWardleyElementCommand(_path, ids[0], "Leaf"));
        var after = await ComponentIdsAsync();

        // Assert.
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Equal("component Leaf [0.5, 0.5]\ncomponent Tea [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\nLeaf->Cup\nevolve Leaf 0.9\n", Read());
        Assert.Equal(ids, after);
    }

    [Fact]
    public async Task RenamingALaterHolder_ChangesItsOwnLineOnly()
    {
        // Arrange.
        Write(TwoTeas + "Tea->Cup\nevolve Tea 0.9\n");
        var ids = await ComponentIdsAsync();

        // Act.
        var renamed = await ExecuteAsync(new RenameWardleyElementCommand(_path, ids[1], "Leaf"));
        var after = await ComponentIdsAsync();

        // Assert.
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Leaf [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\nTea->Cup\nevolve Tea 0.9\n", Read());
        Assert.Equal(ids, after);
    }

    [Fact]
    public async Task RemovingAHolder_LeavesTheOthersTheirIds_AndUndoingItRestoresAllOfThem()
    {
        // Arrange.
        const string text = "component Tea [0.5, 0.5]\ncomponent Tea [0.3, 0.7]\ncomponent Tea [0.2, 0.1]\ncomponent Cup [0.8, 0.2]\nTea->Cup\n";
        Write(text);
        var ids = await ComponentIdsAsync();

        // Act.
        var removed = await ExecuteAsync(new RemoveWardleyElementCommand(_path, ids[1]));
        var after = await ComponentIdsAsync();
        var written = Read();
        await UndoAsync();
        var undone = await ComponentIdsAsync();

        // Assert: a later holder takes its own line only, because nothing attaches to it.
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal("component Tea [0.5, 0.5]\ncomponent Tea [0.2, 0.1]\ncomponent Cup [0.8, 0.2]\nTea->Cup\n", written);
        Assert.Equal([ids[0], ids[2], ids[3]], after);
        Assert.Equal(text, Read());
        Assert.Equal(ids, undone);
    }

    [Fact]
    public async Task RemovingTheFirstHolder_TakesItsLinks_AndTheNextHolderKeepsItsId()
    {
        // Arrange.
        Write(TwoTeas + "Tea->Cup\n");
        var ids = await ComponentIdsAsync();

        // Act.
        var removed = await ExecuteAsync(new RemoveWardleyElementCommand(_path, ids[0]));
        var after = await ComponentIdsAsync();

        // Assert.
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal("component Tea [0.3, 0.7]\ncomponent Cup [0.8, 0.2]\n", Read());
        Assert.Equal([ids[1], ids[2]], after);
    }

    [Fact]
    public async Task AnEditMadeOutsideAdp_GivesTheIdsByOrder()
    {
        // Arrange: three holders, their ids stored by an edit made in ADP.
        Write("component Tea [0.5, 0.5]\ncomponent Tea [0.3, 0.7]\ncomponent Tea [0.2, 0.1]\n");
        await ExecuteAsync(new AddWardleyElementCommand(_path, "component", "Cup", 0.8d, 0.2d));
        var ids = await ComponentIdsAsync();

        // Act: another program removes the first holder.
        Write("component Tea [0.3, 0.7]\ncomponent Tea [0.2, 0.1]\ncomponent Cup [0.8, 0.2]\n");
        await Documents.ReloadAsync(_path, TestContext.Current.CancellationToken);
        var after = await ComponentIdsAsync();

        // Assert: the n-th holder takes the n-th id stored for its name (ruling 7).
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal([ids[0], ids[1], ids[3]], after);
    }

    /// <summary>The text of every `.owm` fixture, those that repeat a key among them.</summary>
    private static IEnumerable<string> Corpus()
    {
        var fixtures = IoPath.Combine(AppContext.BaseDirectory, "Fixtures");
        return Directory.EnumerateFiles(fixtures, "*.owm", SearchOption.AllDirectories).Select(File.ReadAllText);
    }
}
