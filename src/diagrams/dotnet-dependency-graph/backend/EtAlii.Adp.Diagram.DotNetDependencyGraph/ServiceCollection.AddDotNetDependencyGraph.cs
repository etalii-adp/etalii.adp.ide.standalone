using EtAlii.Adp.Context;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Registers the .NET dependency graph module against core's seams. Empty at task 1 by
/// design: the module registers as a type and draws nothing, so every later task is a change
/// to a module that already builds rather than a step in one that never has.
/// </summary>
/// <remarks>
/// <para>
/// <b>What will never appear in this list.</b> No
/// <c>IDiagramToolboxProvider</c>: nothing can be dragged onto a diagram whose contents are
/// derived. No command handlers of its own: the single edit this type permits - a reposition -
/// is core's existing <c>SetRegistrationLayoutCommand</c>, so tech.md's every-change-is-a-command
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

        // A new diagram's body is an empty solution in the newer serialization: valid, openable by
        // the .NET tooling (`dotnet solution list` answers "No projects found in the solution."),
        // and drawn as a graph with nothing in it until the user adds projects with their own
        // tools. It has to exist: Requirement 2.3 names the solution through a body: header, core
        // honours one only for a type that declares a document extension
        // (DiagramFilePair.BodyOf), and the host refuses to start for such a type without an
        // empty document. That this does not breach the never-write-a-solution requirement is
        // not a judgement call: core refuses to create at a name anything already occupies (the
        // File.Exists(siblingPath) || Directory.Exists(siblingPath) guard in
        // EtAlii.Adp.Hierarchy/AddDiagramContextActionProvider.cs), so creation can only ever
        // produce a new file, and the requirement's target - the diagram never mutating the build
        // it describes - is untouched. The ordinary way in is registering a solution that exists.
        services.AddEmptyDocument(Diagram.DependencyGraph.Origin, "<Solution>\r\n</Solution>\r\n");

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
