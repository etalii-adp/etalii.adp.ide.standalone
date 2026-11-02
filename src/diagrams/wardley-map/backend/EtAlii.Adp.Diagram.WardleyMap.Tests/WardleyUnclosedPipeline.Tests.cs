using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// A map with a pipeline whose block never closes takes no new statement (spec 071, ruling 1(a) of 2026-11-02): every
/// statement but a pipeline component is written at the end of the document, which is inside that block, so it would be
/// read as the block's. Every other edit is written as usual.
/// </summary>
public sealed class WardleyUnclosedPipelineTests : IDisposable
{
    private const string Unclosed = "component Cup [0.8, 0.2]\ncomponent Kettle [0.5, 0.4]\npipeline Kettle\n{\n  component Gas [0.3]\ncomponent Power [0.1, 0.7]\n";
    private const string Refusal = "'Kettle' has a pipeline that is missing its closing brace, so nothing can be added after it.";

    private readonly TestFolder _testFolder = TestFolder.Create("wardley-unclosed-pipeline");
    private readonly ServiceProvider _services;
    private readonly string _path;

    public WardleyUnclosedPipelineTests()
    {
        _path = IoPath.Combine(_testFolder.Path, "map.owm");
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        File.WriteAllText(_path, Unclosed);
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    private IWardleyDocumentStore Documents => _services.GetRequiredService<IWardleyDocumentStore>();

    private async Task<string[]> ComponentIdsAsync()
    {
        await Documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        return [.. Documents.Identities(_path).Where(entry => entry.Kind == WardleyIdentityKind.Component).Select(entry => entry.Id)];
    }

    private async Task<CommandResult> ExecuteAsync(ICommand command)
    {
        await Documents.GetOrLoadAsync(_path, TestContext.Current.CancellationToken);
        var history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
        return await history.ExecuteAsync(command, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AComponentAdded_IsRefused_AndTheMapIsUnchanged()
    {
        // Act.
        var added = await ExecuteAsync(new AddWardleyElementCommand(_path, "component", "Water", 0.2d, 0.2d));

        // Assert.
        Assert.False(added.IsSuccess);
        Assert.Equal(Refusal, added.Error);
        Assert.Equal(Unclosed, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALinkAdded_IsRefused_AndTheMapIsUnchanged()
    {
        // Arrange.
        var ids = await ComponentIdsAsync();

        // Act.
        var linked = await ExecuteAsync(new SetWardleyLinkCommand(_path, ids[0], ids[1], WardleyLinkKind.Dependency, Present: true));

        // Assert.
        Assert.False(linked.IsSuccess);
        Assert.Equal(Refusal, linked.Error);
        Assert.Equal(Unclosed, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADefinitionsAdd_IsRefused_AsTheCommandsIs()
    {
        // Arrange.
        var create = new DislChange.Create("Component", "", new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Water", ["visibility"] = 0.2d, ["maturity"] = 0.2d }, null, null);

        // Act.
        var written = await ExecuteAsync(new WriteWardleyTransactionCommand(_path, "addition", [create]));

        // Assert.
        Assert.Equal(Refusal, written.Error);
        Assert.Equal(Unclosed, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMove_IsStillWritten()
    {
        // Arrange.
        var ids = await ComponentIdsAsync();

        // Act.
        var moved = await ExecuteAsync(new MoveWardleyElementCommand(_path, ids[0], 0.7d, 0.2d));

        // Assert.
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(Unclosed.Replace("[0.8, 0.2]", "[0.7, 0.2]", StringComparison.Ordinal), await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }
}
