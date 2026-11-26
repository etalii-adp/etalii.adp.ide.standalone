using System.Text;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Links;
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
    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// The map <paramref name="bytes"/> with a new <c>adp-id</c> on its central topic, as the fix of <c>link.ambiguous-document</c>
    /// gives every candidate but the chosen one (FBL 17.6); nothing else of it changes.
    /// </summary>
    public static byte[]? Renewed(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (DislIds.NewDocumentId(MindmapDefinition.Definition.Specification) is not { } id) return null;
        var marked = bytes.AsSpan().StartsWith(_byteOrderMark);
        var document = MindmapDocument.Parse(Encoding.UTF8.GetString(marked ? bytes[_byteOrderMark.Length..] : bytes), assignMissingIds: false);
        document.Root.SetFreeplaneAttribute(MindmapNode.AdpIdAttribute, id);
        return [.. marked ? _byteOrderMark : [], .. Encoding.UTF8.GetBytes(document.ToText())];
    }

    /// <summary>What a knowledge relation's "Create" can add to a mind map (spec 092, FR-025): a node, as the last child of the central topic.</summary>
    public static DislElementCreation Creation { get; } = new([new LinkElementKind("Node", "node")], MindmapDocument.NewId, WithElement);

    /// <summary>The map <paramref name="bytes"/> with a new last child of its central topic, named <paramref name="name"/>; nothing else of it changes.</summary>
    public static byte[]? WithElement(byte[] bytes, string kind, string id, string name)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var marked = bytes.AsSpan().StartsWith(_byteOrderMark);
        var document = MindmapDocument.Parse(Encoding.UTF8.GetString(marked ? bytes[_byteOrderMark.Length..] : bytes), assignMissingIds: false);
        if (document.Find(id) is not null) return null;
        document.AddChild(document.Root, name).SetId(id);
        return [.. marked ? _byteOrderMark : [], .. Encoding.UTF8.GetBytes(document.ToText())];
    }

    /// <summary>
    /// A new map named <paramref name="baseName"/> (<see cref="MindmapDocument.EmptyDocument"/>) with its central topic's
    /// <c>adp-id</c>, so that it can be linked to from its first save (FBL 17.2), as a new timeline is by its template.
    /// </summary>
    public static string NewDocument(string baseName)
    {
        var document = MindmapDocument.Parse(MindmapDocument.EmptyDocument(baseName), assignMissingIds: false);
        Validate(document, null);
        return document.ToText();
    }

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
