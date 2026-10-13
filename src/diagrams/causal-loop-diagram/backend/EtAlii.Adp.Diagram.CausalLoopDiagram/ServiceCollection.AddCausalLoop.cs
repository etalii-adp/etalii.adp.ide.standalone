using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Registers the causal loop module against core's seams.
/// </summary>
/// <remarks>
/// It grows by group. The document store, the session, the reload seam, the validator and the empty
/// document are registered; the writers and context seams come with the editing group, and the
/// self-organizing layout with its own. The empty document is the one that could never wait -
/// without it the host does not start at all.
/// </remarks>
public static class ServiceCollectionAddCausalLoopExtension
{
    /// <summary>
    /// The origin, as docs/tools.md writes it: <c>causal</c>, and with the <c>-diagram</c>
    /// suffix, both deliberate and both confirmed. See <see cref="Diagram"/>.
    /// </summary>
    public static readonly DiagramOrigin CausalLoopOrigin = new("systems", "causal-loop-diagram");

    public static IServiceCollection AddCausalLoop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store for the process, so two connections on one document share its parse.
        // TryAdd, so a test that registered its own first keeps it.
        services.TryAddSingleton<ICausalLoopDocumentStore, CausalLoopDocumentStore>();
        services.TryAddSingleton<CausalLoopElementMapper>();

        // The registration's layout block is read for the authored positions, so the session is
        // handed the registration path; the project's history, so a drag is one undo away.
        services.AddDiagramSession(CausalLoopOrigin, (provider, opening) => new CausalLoopSession(
            opening.BodyPath,
            opening.RegistrationPath,
            provider.GetRequiredService<ICausalLoopDocumentStore>(),
            provider.GetRequiredService<CausalLoopElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        // The reload seam: a .cld changed by a text editor or a branch switch comes back
        // through here rather than being missed until the next open - and a deleted body as a
        // deletion, not as a reload.
        services.AddDiagramDocumentReloader<ICausalLoopDocumentStore>(CausalLoopOrigin);

        // The starter body a new .cld is created with, registered because core refuses to start a
        // host whose type declares an extension without one. A loop, not two loose variables: a new
        // file should show what the type is for on the first open, and two variables feeding each
        // other positively are the smallest honest reinforcing loop. Its R1 label is arithmetically
        // correct (zero negative links is an even count), so the validator has nothing to say about a
        // file the tool itself just wrote. The base name is not written: the format has no title line.
        // CRLF, the house style, since a new file has no style of its own to keep.
        services.AddEmptyDocument(
            CausalLoopOrigin,
            "causal-loop 1\r\n"
            + "\r\n"
            + "variable population \"Population\"\r\n"
            + "variable births \"Births\"\r\n"
            + "\r\n"
            + "link population -> births +\r\n"
            + "link births -> population +\r\n"
            + "\r\n"
            + "loop R1 \"Births beget births\" population births\r\n");

        // The edit commands, each with a byte-restoring inverse.
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<ICausalLoopDocumentStore>>, RestoreDocumentCommandHandler<ICausalLoopDocumentStore>>();
        services.AddSingleton<ICommandHandler<AddVariableCommand>, AddVariableCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameVariableCommand>, RenameVariableCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveVariableCommand>, RemoveVariableCommandHandler>();
        services.AddSingleton<ICommandHandler<SetVariableLabelCommand>, SetVariableLabelCommandHandler>();
        services.AddSingleton<ICommandHandler<AddLinkCommand>, AddLinkCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLinkPolarityCommand>, SetLinkPolarityCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLinkDelayCommand>, SetLinkDelayCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLinkCurvatureCommand>, SetLinkCurvatureCommandHandler>();
        services.AddSingleton<ICommandHandler<AddVariableAtCommand>, AddVariableAtCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreVariableAdditionCommand>, RestoreVariableAdditionCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLinkWeightCommand>, SetLinkWeightCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLinkLabelCommand>, SetLinkLabelCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveLinkCommand>, RemoveLinkCommandHandler>();
        services.AddSingleton<ICommandHandler<AddLoopCommand>, AddLoopCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLoopNameCommand>, SetLoopNameCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLoopIdentifierCommand>, SetLoopIdentifierCommandHandler>();
        services.AddSingleton<ICommandHandler<SetLoopMembershipCommand>, SetLoopMembershipCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveLoopCommand>, RemoveLoopCommandHandler>();

        // The arrangement: one command for the whole diagram, so one undo puts the registration
        // back rather than one undo per variable.
        services.AddSingleton<ICommandHandler<ArrangeCausalLoopCommand>, ArrangeCausalLoopCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreCausalLoopRegistrationCommand>, RestoreCausalLoopRegistrationCommandHandler>();

        // The context seams: one registration each, resolved by scope and origin.
        services.AddSingleton<IContextActionProvider>(provider => new CausalLoopContextActionProvider(
            provider.GetRequiredService<ICausalLoopDocumentStore>(),
            provider.GetRequiredService<IHistoryStackStore>(),
            provider.GetRequiredService<IDiagramViewportRegistry>()));
        services.AddSingleton<IDiagramToolboxProvider, CausalLoopToolboxProvider>();

        // What makes a canvas selection resolve into a target at all. Without it the element
        // level never resolves, so neither provider above is ever consulted and the whole
        // context surface is silently unreachable in the application - which is exactly how
        // this module shipped.
        services.AddSingleton<IContextSourceResolver, CausalLoopContextSourceResolver>();
        services.AddSingleton<IContextPropertyProvider>(provider => new CausalLoopContextPropertyProvider(
            provider.GetRequiredService<IHistoryStackStore>(),
            provider.GetRequiredService<ICausalLoopDocumentStore>()));

        // The rules, resolved by origin through core's validator registry. This reading only
        // ever reports: the disagreement between a stated label and the arithmetic is a finding,
        // never a correction.
        services.AddSingleton<IDiagramValidator>(_ => new CausalLoopValidator(CausalLoopOrigin));

        return services;
    }
}
