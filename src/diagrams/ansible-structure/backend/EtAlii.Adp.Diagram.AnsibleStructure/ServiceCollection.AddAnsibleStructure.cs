using EtAlii.Adp.Backend.Diagrams;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Registers the whole Ansible structure module against core's seams: the store that reads and
/// watches a folder, the session that streams it, and - as tasks 18 to 20 land - the validator
/// that judges it and the property provider that describes a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read the list for what is not in it.</b> Every other diagram module registers an
/// <see cref="IDiagramDocumentFactory"/>, an <see cref="IDiagramToolboxProvider"/>, an
/// <c>IContextActionProvider</c> and a set of command handlers. This one registers none of the
/// four, and that is the module's whole statement rather than an unfinished job:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>No document factory.</b> The type declares no extension, so there is no empty body to
/// write - the <c>.adp</c> registration is the entirety of what ADP contributes to the folder.
/// </item>
/// <item>
/// <b>No toolbox provider.</b> Nothing can be dragged onto a diagram that writes nothing, and
/// the panel already says what it says for a type with no entries (Requirement 7.3).
/// </item>
/// <item>
/// <b>No action provider and no commands.</b> tech.md's rule is that every functional state
/// change is an <c>ICommand</c>; a module with no state changes therefore has neither. What is
/// not offered is the answer.
/// </item>
/// </list>
/// <para>
/// One call for the module, as <c>AddMindmap</c> and <c>AddC4</c> are for theirs, so the host
/// names the module once rather than listing its seams - and core never names it back:
/// everything resolves by <see cref="DiagramOrigin"/> or <c>ContextScope</c>.
/// </para>
/// </remarks>
public static class ServiceCollectionAddAnsibleStructureExtension
{
    public static IServiceCollection AddAnsibleStructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store per host: it holds one project per registered folder and owns the watcher
        // that keeps each of them true.
        services.AddSingleton<AnsibleProjectReader>();
        services.AddSingleton<AnsibleProjectStore>();
        services.AddSingleton<IAnsibleProjectStore>(provider => provider.GetRequiredService<AnsibleProjectStore>());

        services.AddSingleton<AnsibleElementMapper>(_ => new AnsibleElementMapper());
        services.AddSingleton<IDiagramSessionFactory, AnsibleSessionFactory>();

        // Read-only does not mean silent: the structural mistakes this type can see are exactly
        // the ones that bite at deploy time (Requirement 9). The validator reads the folder
        // directly rather than through the store - validation is a one-shot question, and
        // routing it through the store would leave a watcher behind on every "Validate all".
        services.AddSingleton<IDiagramValidator, AnsibleValidator>();

        return services;
    }
}
