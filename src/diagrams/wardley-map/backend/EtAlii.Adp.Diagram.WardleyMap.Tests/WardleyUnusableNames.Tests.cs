using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// A name the format cannot hold is refused from every path that sets one, with one sentence per
/// reason and the document unchanged (finding B13, Requirement 5.8).
/// </summary>
/// <remarks>
/// Two reasons: <c>//</c> starts a comment, so everything after it is cut and the element is not
/// what was typed; and a name a link could not be read back from, such as <c>Self-service</c>,
/// whose link is never read again, so "Link to" appends it once more each time.
/// </remarks>
public sealed class WardleyUnusableNamesTests : IDisposable
{
    private const string Map = "component Tea [0.5, 0.5]\ncomponent Cup [0.8, 0.2]\npipeline Tea\n{\n  component Pot [0.6]\n}\n";

    private const string Comment = "A name cannot hold '//', which the map reads as the start of a comment.";

    private readonly TestFolder _testFolder = TestFolder.Create("wardley-unusable-names");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly IHistoryStack _history;
    private readonly string _path;

    public WardleyUnusableNamesTests()
    {
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
        _path = IoPath.Combine(_testFolder.Path, "map.owm");
        File.WriteAllText(_path, Map);
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    private static string Unlinkable(string name) => $"A link to or from '{name}' could not be read back, so the map cannot hold that name.";

    public static TheoryData<string, string> Names() => new()
    {
        { "Tea // hot", Comment },
        { "Self-service", Unlinkable("Self-service") },
        { "Add+on", Unlinkable("Add+on") },
    };

    private async Task<string> IdOfAsync(string name)
    {
        await _documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return _documents.Identities(_path).First(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name).Id;
    }

    private async Task AssertRefusedAsync(ICommand command, string sentence)
    {
        await _documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);

        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(sentence, result.Error);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public Task Add_RefusesTheName(string name, string sentence) =>
        AssertRefusedAsync(new AddWardleyElementCommand(_path, "component", name, 0.4d, 0.4d), sentence);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Rename_RefusesTheName(string name, string sentence) =>
        await AssertRefusedAsync(new RenameWardleyElementCommand(_path, await IdOfAsync("Cup"), name), sentence);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task APipelineComponentsName_RefusesTheName(string name, string sentence) =>
        await AssertRefusedAsync(new SetWardleyPipelineMembershipCommand(_path, await IdOfAsync("Tea"), name, Member: true), sentence);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task AnEvolutionsNewName_RefusesTheName(string name, string sentence) =>
        await AssertRefusedAsync(new SetWardleyEvolveCommand(_path, await IdOfAsync("Cup"), Present: true, 0.9d, name), sentence);
}
