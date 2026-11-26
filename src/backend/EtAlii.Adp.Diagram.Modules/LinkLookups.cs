using EtAlii.Adp.Context;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The host's lookup for a diagram's <c>lookup</c> action (DISL 0.12 §9.4, etalii.adp spec 092, T021): opened over the index of the
/// workspace that holds the diagram, narrowed to the action's <c>origins</c>, and the element picked in it read back as the
/// pick the action creates its linked element for. Shared by every diagram module, which only says which operation it runs.
/// </summary>
public static class LinkLookups
{
    /// <summary>Why the lookup cannot open: the diagram is in no workspace the index follows.</summary>
    private const string NoWorkspace = "Adding an element of another document needs the workspace's index, which does not hold this diagram.";

    /// <summary>
    /// The lookup the operation <paramref name="operationId"/> of <paramref name="specification"/> opens for the diagram at
    /// <paramref name="bodyPath"/>; a refusal where the operation holds no lookup or the index holds no workspace for it.
    /// </summary>
    public static ContextExecutionResult Open(ILinkIndex? links, DislSpecification specification, string operationId, string bodyPath)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (specification.LookupOriginsOf(operationId) is not { } origins) return new ContextExecutionFailed($"'{operationId}' opens no lookup.");
        if (links is null || links.WorkspaceOf(bodyPath) is null) return new ContextExecutionFailed(NoWorkspace);
        return new ContextExecutionRequiresLookup(new ContextLookupRequest(
            "Add from another document",
            "mdi-link-plus",
            "Search by name",
            "No element of the workspace has a name like that.",
            query => Search(links, bodyPath, query, origins)));
    }

    /// <summary>
    /// The element <paramref name="value"/> names, picked in the lookup of the diagram at <paramref name="bodyPath"/>: its link and the
    /// name it is cached under, as the lookup offered it; null when the workspace no longer resolves it to an element.
    /// </summary>
    public static DislLookupPick? Picked(ILinkIndex? links, string bodyPath, string value)
    {
        if (links?.WorkspaceOf(bodyPath) is not { } workspace || workspace.Resolve(value) is not { Resolved: true, Element: not null, Refreshed: { } refreshed }) return null;
        return new DislLookupPick(refreshed.Text, refreshed.Name ?? "");
    }

    private static IReadOnlyList<ContextLookupCandidate> Search(ILinkIndex links, string bodyPath, string query, IReadOnlyList<string> origins) =>
        links.WorkspaceOf(bodyPath) is { } workspace
            ? [.. workspace.Search(query, origins, ContextLookupRequest.Shown + 1).Select(candidate => new ContextLookupCandidate(candidate.Link, candidate.Name, candidate.DocumentName, candidate.Folder))]
            : [];
}
