using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The module's whole integration surface, asserted as a surface: eight seams, one method, and
/// nothing in core that names this type (Requirement 12.3).
/// </summary>
public sealed class AddWardleyMapTests : IDisposable
{
    /// <summary>
    /// The core services a host provides, and then the module. AddHierarchy is here because the
    /// source resolver takes core's DiagramFileRouter - a module depending on a core service is
    /// the direction that is allowed; the reverse is what Requirement 12.4 forbids. The catalog
    /// is registered before AddCommands so its TryAddSingleton keeps this one rather than
    /// falling back to a factory that needs a definitions list nothing here supplies.
    /// </summary>
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton<IDiagramDefinitionCatalog>(new WardleyTestDiagramDefinitionCatalog(Diagram.WardleyMap))
        .AddHierarchy()
        .AddDiagrams()
        .AddCommands()
        .AddWardleyMap()
        .BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void AllEightSeamsAreRegistered()
    {
        // Act and assert. Requirement 12.3 lists eight; this is the list.
        Assert.Single(_services.GetServices<IDiagramDocumentFactory>().OfType<WardleyDocumentFactory>());
        Assert.Single(_services.GetServices<IDiagramSessionFactory>().OfType<WardleySessionFactory>());
        Assert.Single(_services.GetServices<IContextSourceResolver>().OfType<WardleyContextSourceResolver>());
        Assert.Single(_services.GetServices<IContextActionProvider>().OfType<WardleyContextActionProvider>());
        Assert.Single(_services.GetServices<IDiagramToolboxProvider>().OfType<WardleyToolboxProvider>());
        Assert.Single(_services.GetServices<IContextPropertyProvider>().OfType<WardleyContextPropertyProvider>());
        Assert.Single(_services.GetServices<IDiagramValidator>().OfType<WardleyValidator>());
        Assert.NotNull(_services.GetService<ICommandHandler<MoveWardleyElementCommand>>());
    }

    [Fact]
    public void EverySeamAnswersForThisModulesOrigin()
    {
        // Act and assert. Core resolves each of these BY ORIGIN, so a seam answering for the
        // wrong one - or for none - is a module core cannot find.
        Assert.Equal(Diagram.WardleyMap.Origin, _services.GetServices<IDiagramSessionFactory>().OfType<WardleySessionFactory>().Single().Origin);
        Assert.Equal(Diagram.WardleyMap.Origin, _services.GetServices<IDiagramToolboxProvider>().OfType<WardleyToolboxProvider>().Single().Origin);
        Assert.Equal(Diagram.WardleyMap.Origin, _services.GetServices<IDiagramValidator>().OfType<WardleyValidator>().Single().Origin);
        Assert.Equal(Diagram.WardleyMap.Origin, _services.GetServices<IDiagramDocumentFactory>().OfType<WardleyDocumentFactory>().Single().Origin);
    }

    [Fact]
    public void TheDocumentFactoryIsThereBecauseTheTypeDeclaresAnExtension()
    {
        // Act. DiagramDocumentFactories.Verify fails startup for a type that declares an
        // extension without a factory to write its body - which is exactly what happened when
        // the extension was declared a task before the factory existed.
        var factories = new DiagramDocumentFactories(_services.GetServices<IDiagramDocumentFactory>());

        // Assert.
        factories.Verify(Diagram.Definitions);
    }

    [Fact]
    public void TheCommandsCanBeRegisteredOnTheirOwn()
    {
        // Act. Requirement 9.7 names AddWardleyMapCommands, and a test that needs only the
        // edits should not have to bring a session factory, a toolbox and a validator with it.
        using var commandsOnly = new ServiceCollection().AddCommands().AddWardleyMapCommands().BuildServiceProvider();

        // Assert.
        Assert.NotNull(commandsOnly.GetService<ICommandHandler<AddWardleyElementCommand>>());
        Assert.NotNull(commandsOnly.GetService<IWardleyDocumentStore>());
        Assert.Empty(commandsOnly.GetServices<IDiagramSessionFactory>());
    }

    [Fact]
    public void OneDocumentStoreServesEverySeam()
    {
        // Act. Requirement 10.6 - an edit made through one connection is visible to another
        // only if they hold one document between them, which holds only if they hold one store.
        var first = _services.GetRequiredService<IWardleyDocumentStore>();
        var second = _services.GetRequiredService<IWardleyDocumentStore>();

        // Assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void CallingItTwiceDoesNotProduceTwoStoresThatDriftApart()
    {
        // Act. TryAdd rather than Add on the store, so a second call is harmless.
        using var twice = new ServiceCollection()
            .AddSingleton<IDiagramDefinitionCatalog>(new WardleyTestDiagramDefinitionCatalog(Diagram.WardleyMap))
            .AddHierarchy()
            .AddDiagrams()
            .AddCommands()
            .AddWardleyMap()
            .AddWardleyMap()
            .BuildServiceProvider();

        // Assert.
        Assert.Single(twice.GetServices<IWardleyDocumentStore>());
    }
}
