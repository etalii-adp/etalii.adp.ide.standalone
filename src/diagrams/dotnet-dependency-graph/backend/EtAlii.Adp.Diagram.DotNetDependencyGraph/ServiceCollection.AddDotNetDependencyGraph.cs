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

        // Task 1 registers nothing: the reader, session, providers and layout arrive with the
        // tasks that build them. The method exists from the start so the definition's Build
        // callback names something real rather than being wired up later.
        return services;
    }
}
