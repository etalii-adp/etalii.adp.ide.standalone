using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Registers the .NET dependency graph module against core's seams: the readers the plugin runs,
/// the store of opened solutions, the new document made from the binding's template, the mapper,
/// the session factory, and the selection and property-grid seams.
/// </summary>
/// <remarks>
/// <para>
/// <b>What will never appear in this list.</b> No
/// <c>IDiagramToolboxProvider</c>: nothing can be dragged onto a diagram whose contents are
/// derived. No command handlers of its own: the single edit this type permits - a reposition -
/// is core's <c>PlaceInRegistrationCommand</c>, so tech.md's every-change-is-a-command
/// rule is satisfied by a command that already exists.
/// </para>
/// <para>
/// One call for the module, as <c>AddAnsibleStructure</c> and <c>AddC4</c> are for theirs, so
/// the host names the module once rather than listing its seams - and core never names it back.
/// </para>
/// </remarks>
public static class ServiceCollectionAddDotNetDependencyGraphExtension
{
    public static IServiceCollection AddDotNetDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The readers: one solution parser, one project parser, one description reader. All
        // three are stateless and read-only, so one of each per host is right.
        services.AddSingleton<SolutionReader>();
        services.AddSingleton<ProjectReader>();
        services.AddSingleton<PackageDescriptionReader>();

        // One store per host: it holds one derived graph per solution, so every session and the
        // property grid read the same graph rather than each deriving its own.
        services.AddSingleton<DependencyGraphStore>();
        services.AddSingleton<IDependencyGraphStore>(provider => provider.GetRequiredService<DependencyGraphStore>());

        // A new diagram's body is the binding's template, an empty solution in the newer
        // serialization under its own extension, .slnx: valid, openable by the .NET tooling
        // (`dotnet sln list` answers "No projects found in the solution."), and drawn as a graph
        // with nothing in it until the user adds projects with their own tools (Requirement 5.3,
        // Q4). It has to exist: the host refuses to start for a type that declares a document
        // extension without a new document. That this does not breach the never-write-a-solution
        // requirement is not a judgement call: core refuses to create at a name either
        // serialization already occupies (DiagramAddOptions.PlanNewDocument in EtAlii.Adp.Hierarchy),
        // so creation can only ever produce a new file. The ordinary way in is registering a
        // solution that exists.
        services.AddSingleton<IDiagramDocumentFactory>(new TemplateDocumentFactory(Diagram.DependencyGraph, DotNetBody.Binding));

        services.AddSingleton<DependencyElementMapper>();
        services.AddSingleton<IDiagramSessionFactory, DotNetDependencyGraphSessionFactory>();

        // One resolver makes every project, package and edge selectable, so the property grid,
        // the ribbon and the menu answer for one like any other element. The context service
        // itself is untouched.
        services.AddSingleton<IContextSourceResolver, DotNetContextSourceResolver>();

        // Property-grid Requirement 4 at 100%: every row this contributes is read-only, and
        // every reason names what would have to change instead. ContextPropertyResolver refuses
        // a write to any of them server-side, so the markings are enforced, not styled.
        services.AddSingleton<IContextPropertyProvider, DotNetContextPropertyProvider>();

        return services;
    }
}
