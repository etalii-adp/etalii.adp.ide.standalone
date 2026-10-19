using EtAlii.Adp.Files;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Everything a <see cref="HierarchyModel"/> asks the disk: a folder's entries, where its
/// registrations nest and what state each entry is in, and what an event's path is now. It reads
/// and changes nothing of the model's, so the model runs it outside its lock (FR-066) and applies
/// what it read, through <see cref="HierarchyTree"/>, under it.
/// </summary>
internal sealed class HierarchyFolderReader
{
    private readonly string _rootPath;
    private readonly ToolRouter _tools;
    private readonly DiagramStateDecorator _states;

    public HierarchyFolderReader(string rootPath, ToolRouter tools)
    {
        _rootPath = rootPath;
        _tools = tools;
        _states = new DiagramStateDecorator(rootPath, tools.Diagrams, tools.Editors, ResolvedBodyNameOf);
    }

    /// <summary>
    /// What the disk says about an event's path and its folder: whether the path exists, whether
    /// it is a folder with anything in it, and whether its folder still holds anything.
    /// </summary>
    public sealed record EventFacts(bool Exists, bool IsFolder, bool HasAnyChild, bool ParentHasAnyChild);

    /// <summary>
    /// What one folder's scan read from disk and decided from it: its entries, or the failure that
    /// made it unreadable, and where its registrations nest and what state each entry is in.
    /// </summary>
    public sealed record FolderRead(IReadOnlyList<DiskEntry>? Entries, Exception? Failure, FolderView View);

    public readonly record struct DiskEntry(string Path, string Name, bool IsFolder, bool HasAnyChild);

    /// <summary>Where one folder's registrations nest, and the state of each entry in it.</summary>
    public sealed record FolderView(IReadOnlyList<NestedEntry> Placements, IReadOnlyList<DiagramStateDecorator.Decision> States);

    /// <summary>What the disk says about an event's path, or null for an event that needs none of it.</summary>
    public static EventFacts? FactsOf(FileEvent heard)
    {
        // A rename needs none of it, unless it publishes a scratch file and so is a create.
        var needed = heard.Kind switch
        {
            FileEventKind.Created or FileEventKind.Deleted => true,
            FileEventKind.Renamed => WatcherEventNormalizer.IsTransient(heard.OldPath),
            _ => false,
        };
        if (!needed || WatcherEventNormalizer.IsTransient(heard.Path))
        {
            return null;
        }

        var isFolder = Directory.Exists(heard.Path);
        var parent = IoPath.GetDirectoryName(heard.Path);
        return new EventFacts(
            isFolder || File.Exists(heard.Path),
            isFolder,
            isFolder && SafeHasAnyChild(heard.Path),
            parent is not null && SafeHasAnyChild(parent));
    }

    /// <summary>Reads a folder's entries, and decides their nesting and states.</summary>
    public FolderRead Read(string folderPath, string? folderName)
    {
        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFileSystemEntries(folderPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return new FolderRead(null, ex, new FolderView([], []));
        }

        var entries = found
            .Where(entry => IsContained(entry) && !WatcherEventNormalizer.IsTransient(entry))
            .Select(entry =>
            {
                var isFolder = Directory.Exists(entry);
                return new DiskEntry(entry, IoPath.GetFileName(entry), isFolder, isFolder && SafeHasAnyChild(entry));
            })
            .ToList();
        return new FolderRead(entries, null, ReadView(folderPath, folderName, entries.Select(entry => (entry.Name, entry.IsFolder)).ToList()));
    }

    /// <summary>
    /// Where a folder's registrations nest and what state each entry is in, decided from its names:
    /// both read the registrations, and the states peek into the folders in it.
    /// </summary>
    public FolderView ReadView(string folderPath, string? folderName, IReadOnlyList<(string Name, bool IsFolder)> names) =>
        new(
            HierarchyNesting.Assign(names, name => ResolvedBodyNameOf(folderPath, name)),
            _states.Decide(folderName, folderPath, names));

    // The link probe touches the disk, and the entry may be gone by the time it runs: a
    // multi-file delete (a git checkout, say) raises one watcher event per file, and each
    // event re-resolves every tracked selection - whose files may already have vanished
    // while their model entries have not. This runs on the watcher's thread, where an
    // unhandled exception kills the whole process (found by the diagram-workspace-tabs manual
    // pass). A gone entry is therefore not contained - it resolves to nothing, same as an
    // unknown one - which PathContainment alone would not say, since it keeps the missing part
    // of a path as written; and PathContainment answers a path it cannot probe with "not
    // inside" rather than throwing.
    public bool IsContained(string path) =>
        (File.Exists(path) || Directory.Exists(path)) && PathContainment.IsInside(_rootPath, path);

    private static bool SafeHasAnyChild(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any(entry => !WatcherEventNormalizer.IsTransient(entry));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The body file name a registration resolves inside its own folder, or null: no resolution,
    /// or a body living elsewhere. Which tool's document the registration names is
    /// <see cref="ToolRouter"/>'s answer: a diagram's body, or a designer's, whose document is two
    /// files just the same and is shown as one entry the same way. Only same-folder bodies nest,
    /// since a header can point across folders and relocating an entry into another folder's
    /// subtree is not this spec.
    /// </summary>
    private string? ResolvedBodyNameOf(string folderPath, string fileName)
    {
        if (!DiagramFilePair.IsRegistrationFile(fileName))
        {
            return null;
        }

        var registration = IoPath.Combine(folderPath, fileName);
        var bodyPath = _tools.Route(registration, _rootPath) switch
        {
            // A type with no sibling routes to the registration itself, which is no body to nest.
            DiagramRouted { Definition.HasDocumentSibling: true } diagram => diagram.BodyPath,
            DesignerRouted designer => designer.BodyPath,
            _ => null,
        };

        if (string.IsNullOrEmpty(bodyPath))
        {
            return null;
        }

        return string.Equals(IoPath.GetDirectoryName(bodyPath), folderPath, StringComparison.OrdinalIgnoreCase)
            ? IoPath.GetFileName(bodyPath)
            : null;
    }
}
