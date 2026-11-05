using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// A rename keeps the id of the renamed element and of every link, pipeline and pipeline
/// component whose key holds its name, and an undone rename restores the ids it had (finding
/// B10, Requirement 6.3).
/// </summary>
public sealed class WardleyRenameIdentitiesTests : IDisposable
{
    private const string Map =
        "component Kettle [0.43, 0.35]\n" +
        "component Cup [0.80, 0.20]\n" +
        "pipeline Kettle\n" +
        "{\n" +
        "  component Electric [0.63]\n" +
        "}\n" +
        "Cup->Kettle\n" +
        "Kettle+>Cup\n";

    private readonly TestFolder _testFolder = TestFolder.Create("wardley-rename-identities");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly IHistoryStack _history;
    private readonly string _path;

    public WardleyRenameIdentitiesTests()
    {
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
        _path = IoPath.Combine(_testFolder.Path, "map.owm");
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    /// <summary>Every id the store holds, as kind and id in document order.</summary>
    private async Task<(string Kind, string Id)[]> IdsAsync()
    {
        await _documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return _documents.Identities(_path).Select(entry => (entry.Kind, entry.Id)).ToArray();
    }

    private async Task<string> IdOfAsync(string name)
    {
        await _documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return _documents.Identities(_path).First(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name).Id;
    }

    [Fact]
    public async Task ARename_KeepsTheIdsOfTheElement_ItsLinks_ItsPipeline_AndItsPipelineComponents()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, Map, TestContext.Current.CancellationToken);
        var before = await IdsAsync();

        // Act.
        var renamed = await _history.ExecuteAsync(
            new RenameWardleyElementCommand(_path, await IdOfAsync("Kettle"), "Boiler"),
            TestContext.Current.CancellationToken);
        var after = await IdsAsync();

        // Assert: one entry per element, each with the id it had.
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Contains("pipeline Boiler", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task AnUndoneRename_RestoresTheIdsItHad()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, Map, TestContext.Current.CancellationToken);
        var before = await IdsAsync();
        await _history.ExecuteAsync(
            new RenameWardleyElementCommand(_path, await IdOfAsync("Kettle"), "Boiler"),
            TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        var restored = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        var undone = await IdsAsync();
        await _history.RedoAsync(TestContext.Current.CancellationToken);
        var redone = await IdsAsync();

        // Assert.
        Assert.Equal(Map, restored);
        Assert.Equal(before, undone);
        Assert.Equal(before, redone);
    }
}
