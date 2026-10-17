using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// What the module's registrations open: the session and the reload seam are registrations rather than
/// classes of this module, so it is here that they are seen to reach the origin's files.
/// </summary>
public class ServiceCollectionAddCausalLoopTests
{
    private static ServiceProvider Build() =>
        new ServiceCollection()
            .AddSingleton<IDiagramViewportRegistry, DiagramViewportRegistry>()
            .AddCommands().AddHierarchyCommandHandlers()
            .AddCausalLoop()
            .BuildServiceProvider();

    [Fact]
    public async Task ASession_IsOpenedOnTheBody_AndDrawsIt()
    {
        // Arrange: a bare .cld routes with no registration, so opening must not need one.
        await using var provider = Build();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>())
            .Find(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), "root", CausalLoopDocumentStoreTests.OnCall, null);

        // Assert.
        Assert.IsType<CausalLoopSession>(session);
        Assert.NotEmpty(session.Baseline());
    }

    [Fact]
    public async Task ASession_IsHandedTheRegistration_AndDrawsWhereItPlacedAVariable()
    {
        // Arrange: the same body opened bare and through a registration that places one variable.
        await using var provider = Build();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>())
            .Find(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin)!;
        using var folder = TestFolder.Create("causal-loop-registration");
        var registration = folder.Combine("on-call.adp");
        await File.WriteAllTextAsync(registration, "systems/causal-loop-diagram\r\n", TestContext.Current.CancellationToken);
        RegistrationLayout.SetPosition(registration, "variable:incidents", new RegistrationPosition(4321, 1234));

        // Act.
        await using var bare = factory.Open(ShortGuid.NewShortGuid(), "root", CausalLoopDocumentStoreTests.OnCall, null);
        await using var placed = factory.Open(ShortGuid.NewShortGuid(), "root", CausalLoopDocumentStoreTests.OnCall, registration);

        // Assert: only the registration's position moves it.
        var before = Assert.Single(Drawn(bare), element => element.Id == "variable:incidents");
        var after = Assert.Single(Drawn(placed), element => element.Id == "variable:incidents");
        Assert.NotEqual(before.X, after.X);
    }

    private static IEnumerable<DiagramElement> Drawn(IDiagramSession session) =>
        session.Baseline().OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements);

    [Fact]
    public async Task TheReloadSeam_ReachesTheModulesOneStore()
    {
        // Arrange.
        await using var provider = Build();
        var store = provider.GetRequiredService<ICausalLoopDocumentStore>();
        var reloader = Assert.Single(
            provider.GetServices<IDiagramDocumentReloader>(),
            candidate => candidate.Origin == ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin);
        var changed = new List<string>();
        store.Changed += (_, args) => changed.Add(args.Path);
        _ = store.GetOrLoad(CausalLoopDocumentStoreTests.OnCall);

        // Act.
        reloader.BodyDeleted("root", CausalLoopDocumentStoreTests.OnCall);

        // Assert.
        Assert.Equal([CausalLoopDocumentStoreTests.OnCall], changed);
    }
}
