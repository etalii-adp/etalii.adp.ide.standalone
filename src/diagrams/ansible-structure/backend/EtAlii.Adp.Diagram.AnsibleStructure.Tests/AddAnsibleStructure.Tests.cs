using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// What the module registers - and, at least as importantly, what it does not.
/// </summary>
/// <remarks>
/// The absences are asserted rather than left to inspection. A later reader meeting four
/// registrations where every other module has eight will reasonably wonder whether the module
/// is finished; these tests answer that it is, and turn "we chose not to" into something that
/// fails out loud if someone adds one back for symmetry.
/// </remarks>
public class AddAnsibleStructureTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        // AddCommands first, as the host and every sibling module's test do: the session
        // factory resolves the project's history store from there for its one edit, and the
        // module registers no history of its own.
        services.AddCommands().AddHierarchyCommandHandlers();
        services.AddAnsibleStructure();
        return services.BuildServiceProvider();
    }

    // ---- what is registered ----------------------------------------------------------------

    [Fact]
    public void TheModule_RegistersItsSessionFactory_ForItsOwnOrigin()
    {
        // Act.
        using var provider = Build();

        // Assert.
        var factory = Assert.Single(provider.GetServices<IDiagramSessionFactory>());
        Assert.Equal(Diagram.AnsibleStructure.Origin, factory.Origin);
    }

    [Fact]
    public void TheStore_IsOnePerHost_SoOneFolderIsReadOnce()
    {
        // Act.
        using var provider = Build();

        // Assert.
        // Two connections to one project must not each hold their own reading of it.
        Assert.Same(provider.GetRequiredService<IAnsibleProjectStore>(), provider.GetRequiredService<IAnsibleProjectStore>());
        Assert.Same(provider.GetRequiredService<AnsibleProjectStore>(), provider.GetRequiredService<IAnsibleProjectStore>());
    }

    [Fact]
    public void TheMapperAndReader_AreResolvable()
    {
        // Act.
        using var provider = Build();

        // Assert.
        Assert.NotNull(provider.GetRequiredService<AnsibleElementMapper>());
        Assert.NotNull(provider.GetRequiredService<AnsibleProjectReader>());
    }

    // ---- what is deliberately absent ---------------------------------------------------------

    [Fact]
    public void TheModule_RegistersNoDocumentFactory()
    {
        // Act.
        using var provider = Build();

        // Assert.
        // The type declares no extension, so there is no empty body to write: the .adp
        // registration is the entirety of what ADP contributes to the folder.
        Assert.Empty(provider.GetServices<IDiagramDocumentFactory>());
    }

    [Fact]
    public void TheModule_RegistersNoToolboxProvider()
    {
        // Act.
        using var provider = Build();

        // Assert.
        // Nothing can be dragged onto a diagram that writes nothing (Requirement 7.3).
        Assert.Empty(provider.GetServices<IDiagramToolboxProvider>());
    }

    [Fact]
    public void TheModule_RegistersNoActionProvider()
    {
        // Act.
        using var provider = Build();

        // Assert.
        // tech.md's rule is that every functional state change is an ICommand. A module with
        // no state changes has neither actions nor commands, and what is not offered is the
        // answer (Requirement 11.2).
        Assert.Empty(provider.GetServices<IContextActionProvider>());
    }

    [Fact]
    public void TheModule_RegistersNoCommandHandler()
    {
        // Act.
        var services = new ServiceCollection();
        services.AddAnsibleStructure();

        // Assert.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("ICommandHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingRegisteredIsNamedAsAWriter()
    {
        // Act.
        var services = new ServiceCollection();
        services.AddAnsibleStructure();

        // Assert.
        // A blunt instrument, and worth having: this module's whole claim is that it writes
        // nothing, so a type named Writer, Saver or Command appearing in its registrations is
        // a question somebody should have to answer out loud.
        Assert.DoesNotContain(services, descriptor =>
            (descriptor.ImplementationType?.Name ?? "") is var name &&
            (name.Contains("Writer", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Saver", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Command", StringComparison.OrdinalIgnoreCase)));
    }
}
