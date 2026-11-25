using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Load-time validation of a mind map (FBL 0.23 §17.4, <c>mindmap.md</c> 3.8, etalii.adp spec 092, R6 and R7): the central
/// topic's <c>adp-id</c> where it has none, and each linked node's <c>adp-link</c> and cached name where they differ from what
/// resolution finds.
/// </summary>
/// <remarks>
/// <b>What it finds is changed in the map in memory and written with the next save, never on opening</b>, as the ids a map's
/// nodes lack have always been (<see cref="MindmapDocument.Parse"/>). Like those ids, an undo of that first edit takes back the
/// edit and leaves what opening found: a mind map's undo is the inverse of its command, not the text the file held.
/// </remarks>
/// <summary>What opening a map found to change in it, until a save writes it.</summary>
/// <param name="File">The text the file held.</param>
/// <param name="Pending">The text of the map as opening changed it, which the canvas shows.</param>
public sealed record MindmapOpened(string File, string Pending);

internal static class MindmapOpening
{
    /// <summary>Changes <paramref name="document"/> as opening finds it should be; <paramref name="workspace"/> null leaves its links as they are.</summary>
    public static void Validate(MindmapDocument document, LinkWorkspace? workspace)
    {
        var root = document.Root;
        if (root.AdpId is null && DislIds.NewDocumentId(MindmapDefinition.Definition.Specification) is { } id)
        {
            root.SetFreeplaneAttribute(MindmapNode.AdpIdAttribute, id);
        }

        if (workspace is null) return;
        foreach (var node in document.Nodes)
        {
            if (node.AdpLink is not { Length: > 0 } link || workspace.Resolve(link) is not { Resolved: true, Refreshed: { } refreshed }) continue;
            if (refreshed.Text != link) node.SetFreeplaneAttribute(MindmapNode.AdpLinkAttribute, refreshed.Text);
            // A linked node's TEXT is its cached name (mindmap.md 3.8).
            if (refreshed.Name is { } name && node.Text.Length > 0 && node.Text != name) node.SetText(name);
        }
    }
}
