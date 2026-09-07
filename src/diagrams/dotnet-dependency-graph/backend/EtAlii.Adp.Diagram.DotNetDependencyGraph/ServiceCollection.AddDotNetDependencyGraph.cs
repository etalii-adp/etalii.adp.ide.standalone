using EtAlii.Adp.Context;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Registers the .NET dependency graph module against core's seams. Empty at task 1 by
/// design: the module registers as a type and draws nothing, so every later task is a change
/// to a module that already builds rather than a step in one that never has.
/// </summary>
/// <remarks>
/// <para>
/// <b>What will never appear in this list.</b> No <c>IDiagramDocumentFactory</c>: the body is
/// a solution the user's build owns, and this type creates one no more than it edits one. No
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
