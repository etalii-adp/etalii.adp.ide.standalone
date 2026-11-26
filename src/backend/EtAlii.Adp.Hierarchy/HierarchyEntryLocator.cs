using EtAlii.Adp.Context;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Finds a document's entry in a connection's own <see cref="HierarchyModel"/> by walking its folders from the root, listing each
/// as the explorer would, so the entry it answers is one that connection resolves (etalii.adp spec 092, FR-014). A body nested
/// under its registration is found among the registration's children.
/// </summary>
public sealed class HierarchyEntryLocator(IHierarchyModelStore hierarchyModelStore, ToolRouter tools) : IContextEntryLocator
{
    /// <inheritdoc />
    public ContextEntryLocation? Locate(ShortGuid watchId, ProjectRoot root, string fullPath)
    {
        var relative = IoPath.GetRelativePath(root.FullPath, fullPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || IoPath.IsPathRooted(relative)) return null;

        var model = hierarchyModelStore.GetOrCreate(watchId, root);
        var segments = relative.Split([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        ShortGuid? folder = null;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (model.ListChildren(folder).FirstOrDefault(entry => entry.IsFolder && entry.Name == segments[index]) is not { } next) return null;
            folder = next.Id;
        }

        return EntryNamed(model, folder, segments[^1]) is { } found ? new ContextEntryLocation(found.Id, MimeTypeOf(fullPath)) : null;
    }

    /// <summary>The file <paramref name="name"/> in <paramref name="folder"/>, or nested under one of its registrations.</summary>
    private static EntryNode? EntryNamed(HierarchyModel model, ShortGuid? folder, string name)
    {
        var listing = model.ListChildren(folder);
        return listing.FirstOrDefault(entry => !entry.IsFolder && entry.Name == name)
            ?? listing
                .Where(entry => entry is { IsFolder: false, HasChildren: true })
                .SelectMany(entry => model.ListChildren(entry.Id))
                .FirstOrDefault(entry => !entry.IsFolder && entry.Name == name);
    }

    /// <summary>Which tool opens the file, as an activation's entry detail says it.</summary>
    private string MimeTypeOf(string fullPath) =>
        tools.Route(fullPath) switch
        {
            DiagramRouted diagram => diagram.Definition.Origin.MimeType,
            DesignerRouted designer => designer.Definition.Origin,
            EditorRouted editor => $"editor/{editor.Definition.Id}",
            _ => "",
        };
}
