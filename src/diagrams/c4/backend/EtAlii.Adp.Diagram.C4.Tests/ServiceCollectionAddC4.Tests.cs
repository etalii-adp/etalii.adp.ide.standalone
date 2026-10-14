using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What the module's registrations open: the reload seam is a registration rather than a class of this
/// module, so it is here that it is seen to reach the store every C4 type shares.
/// </summary>
public class ServiceCollectionAddC4Tests
{
    public static TheoryData<string> Types() => ["system-landscape", "context", "container", "component", "dynamic", "deployment"];

    [Theory]
    [MemberData(nameof(Types))]
    public async Task EveryType_HasOneReloadSeam_OverTheOneStore(string type)
    {
        // Arrange.
        await using var provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        using var folder = TestFolder.Create("c4-reload");
        var body = folder.Combine("courier.dsl");
        File.Copy(C4DocumentStoreTests.Courier, body);
        var store = provider.GetRequiredService<IC4DocumentStore>();
        var reloader = Assert.Single(provider.GetServices<IDiagramDocumentReloader>(), candidate => candidate.Origin == new DiagramOrigin("c4", type));
        var changed = new List<string>();
        store.Changed += (_, args) => changed.Add(args.Path);
        Assert.NotEmpty(store.GetOrLoad(body).Model.Elements);
        File.Delete(body);

        // Act.
        reloader.BodyDeleted(folder.Path, body);

        // Assert: the deletion reached the store the sessions read, and ended its model.
        Assert.Equal([body], changed);
        Assert.Empty(store.GetOrLoad(body).Model.Elements);
    }
}
