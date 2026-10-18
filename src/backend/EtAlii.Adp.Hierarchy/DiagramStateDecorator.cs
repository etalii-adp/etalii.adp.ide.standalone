using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Decides every entry's <see cref="EntryDiagramState"/> for one scanned folder - its files against
/// their sibling set, the folder itself against its contents, and each folder in it from a peek at
/// its files (small-refinements Requirements 3.1, 3.2, 3.6). The decision is
/// <see cref="EntryDiagramStates.Decide"/>; this is the wiring, over the same name set the nesting
/// pass uses, with each registration's body resolved once rather than per file. Which tool a
/// registration names is <see cref="ToolRouter"/>'s answer, reached through the body names the
/// model resolves; which extensions the diagram types claim, and which of them take a folder as
/// their subject, are the diagram router's own questions. It decides and changes nothing, so the
/// model runs it outside its lock and applies the states, and raises their updates, under it.
/// </summary>
internal sealed class DiagramStateDecorator
{
    private readonly string _rootPath;
    private readonly DiagramFileRouter _router;
    private readonly EditorResolver _editors;
    private readonly Func<string, string, string?> _bodyNameOf;

    /// <param name="rootPath">The project root a <c>body:</c> header must stay inside.</param>
    /// <param name="router">Which extensions the diagram types claim, and which take a folder as their subject.</param>
    /// <param name="editors">Which extensions an editor claims.</param>
    /// <param name="bodyNameOf">The body file name a registration in a folder resolves inside that folder, or null.</param>
    public DiagramStateDecorator(string rootPath, DiagramFileRouter router, EditorResolver editors, Func<string, string, string?> bodyNameOf)
    {
        _rootPath = rootPath;
        _router = router;
        _editors = editors;
        _bodyNameOf = bodyNameOf;
    }

    /// <summary>
    /// One entry's decided state, by its name in the folder; <paramref name="IsTheFolder"/> marks
    /// the folder's own, whose change is raised even on a listing.
    /// </summary>
    public readonly record struct Decision(string Name, EntryDiagramState State, bool IsTheFolder);

    /// <summary>
    /// The states of the entries living in <paramref name="folderPath"/>, and of the folder itself
    /// when it is not the root. Reads the disk: the registrations, and the files of each folder in it.
    /// </summary>
    /// <param name="folderName">The folder's own name; null for the root, which has no entry.</param>
    /// <param name="folderPath">The folder.</param>
    /// <param name="contained">The entries living in it, whatever their parent.</param>
    public IReadOnlyList<Decision> Decide(string? folderName, string folderPath, IReadOnlyList<(string Name, bool IsFolder)> contained)
    {
        var decisions = new List<Decision>();
        var names = contained.Select(entry => entry.Name).ToList();

        // Each registration's resolution and routing, once: rule 2 would otherwise re-read
        // every registration per sibling file (the NFR Performance clause).
        var bodyByRegistration = names
            .Where(DiagramFilePair.IsRegistrationFile)
            .ToDictionary(name => name, name => _bodyNameOf(folderPath, name), StringComparer.OrdinalIgnoreCase);

        foreach (var file in contained.Where(entry => !entry.IsFolder))
        {
            decisions.Add(new Decision(file.Name, EntryDiagramStates.Decide(
                file.Name, isFolder: false, names, BodyNameOf, _router.ClaimsExtensionOf, _editors.IsClaimed, DeclaresFolderSubject), IsTheFolder: false));
        }

        // The folder's own state comes from its contents, so it is decided here - in ITS scan -
        // rather than in its parent's, which never enumerates it. The root has no entry. Its
        // update is raised even on a listing-triggered scan: the listing's response carries the
        // folder's CHILDREN, never the folder itself, so the watch stream is the only channel this
        // change can reach the client on - found live, where the folder stayed neutral after
        // expanding it.
        if (folderName is not null)
        {
            decisions.Add(new Decision(folderName, EntryDiagramStates.Decide(
                folderName, isFolder: true, names, BodyNameOf, _router.ClaimsExtensionOf, _editors.IsClaimed, DeclaresFolderSubject), IsTheFolder: true));
        }

        // And each CONTAINED folder's state, from a one-level peek at its files - the colour
        // has to be right on first paint, before the folder is ever expanded, and until it is
        // expanded no scan of its own will run. Found in the field: a folder holding a
        // folder-subject .adp showed its colour only after being opened. Only file names are
        // read; the folder is not synced, and once it is, its own scan decides the same way.
        foreach (var sub in contained.Where(entry => entry.IsFolder))
        {
            var subPath = IoPath.Combine(folderPath, sub.Name);
            string[] subNames;
            try
            {
                subNames = Directory.EnumerateFiles(subPath).Select(IoPath.GetFileName).OfType<string>().ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue; // Vanished or unreadable mid-scan: its state stays what it was.
            }

            decisions.Add(new Decision(sub.Name, EntryDiagramStates.Decide(
                sub.Name, isFolder: true, subNames, name => _bodyNameOf(subPath, name), _router.ClaimsExtensionOf, _editors.IsClaimed, registration => DeclaresFolderSubjectIn(subPath, registration)), IsTheFolder: false));
        }

        return decisions;

        bool DeclaresFolderSubject(string registration) => DeclaresFolderSubjectIn(folderPath, registration);

        string? BodyNameOf(string registration) => bodyByRegistration.GetValueOrDefault(registration);
    }

    // An unreadable registration routes to DiagramUnreadable, which simply is not a
    // folder-subject type: neutral, never an exception out of the scan. The catalog probe
    // keeps deployments without folder-subject types from paying any reads at all.
    private bool DeclaresFolderSubjectIn(string folderPath, string registration) =>
        _router.HasFolderSubjectTypes
        && _router.Route(IoPath.Combine(folderPath, registration), _rootPath) is DiagramRouted { Definition.HasFolderSubject: true };
}
