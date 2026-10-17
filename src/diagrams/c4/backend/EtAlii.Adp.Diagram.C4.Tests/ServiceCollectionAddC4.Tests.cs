using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What the module's registrations open: the reload seam is a registration rather than a class of this
/// module, and the element resolver is registered once per type, so it is here that they are seen to
/// reach the store every C4 type shares and to answer for every type's files.
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
        var reloader = Assert.Single(provider.GetServices<IDiagramDocumentReloader>(), candidate => candidate.Origin == new ToolOrigin("c4", type));
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

    [Theory]
    [MemberData(nameof(Types))]
    public async Task EveryType_HasAnElementResolver_ForItsFiles(string type)
    {
        // Arrange: the courier model, selected inside a registration of this type.
        await using var provider = new ServiceCollection()
            .AddSingleton<IDiagramDefinitionCatalog, C4ExamplesStubCatalog>()
            .AddSingleton<DiagramFileRouter>()
            .AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        using var folder = TestFolder.Create("c4-resolvers");
        var body = folder.Combine("courier.dsl");
        File.Copy(C4DocumentStoreTests.Courier, body);
        var registration = folder.Combine("courier.adp");
        await File.WriteAllTextAsync(registration, $"c4/{type}\r\nbody: courier.dsl\r\n", TestContext.Current.CancellationToken);
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            ["courier.adp"],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration, IsContainer: false, SourceId: default, folder.Path, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);
        var element = new ContextSource { ElementId = new ElementId { Value = "tracking" } };

        // Act.
        var resolved = new List<ContextResolvedLevel>();
        foreach (var resolver in provider.GetServices<IContextSourceResolver>().Where(candidate => candidate.CanResolve(element)))
        {
            if (await resolver.ResolveAsync(ShortGuid.NewShortGuid(), folder.Path, element, [], parent, TestContext.Current.CancellationToken) is ResolvedContextLevel level)
            {
                resolved.Add(level.Level);
            }
        }

        // Assert: exactly one resolver answers, for this type, with the body the registration names.
        var only = Assert.Single(resolved);
        Assert.Equal(new ToolOrigin("c4", type), only.Target.Origin);
        Assert.Equal(body, only.Target.ResolvedFullPath);
    }
}
