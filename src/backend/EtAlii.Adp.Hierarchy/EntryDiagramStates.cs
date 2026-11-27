using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The one place an entry's <see cref="EntryDiagramState"/> is decided (small-refinements
/// Requirements 3.1, 3.2, 3.3, 3.5). Pure over the names it is given: the caller hands it the
/// name set its folder scan already holds, and the routing knowledge arrives as the router's
/// and resolver's own queries - never a client-side table, and never a filesystem probe here.
/// </summary>
public static class EntryDiagramStates
{
    /// <param name="name">The entry's file or folder name.</param>
    /// <param name="isFolder">Whether the entry is a folder.</param>
    /// <param name="names">
    /// For a file, its siblings' names; for a folder, its own contents' names. The two rules
    /// read different sets, and the caller's scan holds both.
    /// </param>
    /// <param name="resolvedBodyNameOf">
    /// The body file name a registration among <paramref name="names"/> resolves in its own
    /// folder, or null - the resolution the scan already performs for nesting.
    /// </param>
    /// <param name="claimsExtension">
    /// <see cref="DiagramFileRouter.ClaimsExtensionOf"/>: a diagram type declares the file's
    /// extension, shared or not.
    /// </param>
    /// <param name="editorClaims">
    /// <see cref="EditorResolver.IsClaimed"/>: a non-fallback editor claims the file.
    /// </param>
    /// <param name="declaresFolderSubject">
    /// Whether a registration among a folder's contents names a type with a folder subject.
    /// </param>
    /// <param name="designerClaims">
    /// Whether an installed designer type claims the file by its own content, so that it opens as that type's document with
    /// no registration (spec 093); none when left out.
    /// </param>
    public static EntryDiagramState Decide(
        string name,
        bool isFolder,
        IReadOnlyCollection<string> names,
        Func<string, string?> resolvedBodyNameOf,
        Func<string, bool> claimsExtension,
        Func<string, bool> editorClaims,
        Func<string, bool> declaresFolderSubject,
        Func<string, bool>? designerClaims = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(names);

        if (isFolder)
        {
            // Registered when a registration INSIDE it declares a folder subject - the shape
            // the code actually has (HierarchyNesting: such a registration's subject IS its
            // parent), correcting Requirement 3.1's parenthetical. Never Potential: there is
            // no meaningful "this folder could be a diagram" claim, and inventing one would
            // green every directory in the tree.
            return names.Any(contained => DiagramFilePair.IsRegistrationFile(contained) && declaresFolderSubject(contained))
                ? EntryDiagramState.Registered
                : EntryDiagramState.Unspecified;
        }

        // Rule 1: the file is itself a registration.
        if (DiagramFilePair.IsRegistrationFile(name))
        {
            return EntryDiagramState.Registered;
        }

        // Rule 2: a registration governs it - a same-named .adp beside it, or a sibling
        // registration whose body: resolution lands on this very file.
        var sameNamed = IoPath.ChangeExtension(name, DiagramFileName.Extension);
        var registrations = names.Where(DiagramFilePair.IsRegistrationFile);
        foreach (var registration in registrations)
        {
            if (string.Equals(registration, sameNamed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedBodyNameOf(registration), name, StringComparison.OrdinalIgnoreCase))
            {
                return EntryDiagramState.Registered;
            }
        }

        // A file a designer type claims by its own content is that type's document, as a registered one is (spec 093).
        if (designerClaims?.Invoke(name) == true)
        {
            return EntryDiagramState.Registered;
        }

        // Rules 3 and 4: registrable, or claimed by an editor - potential either way. Rule 5:
        // nothing claims it (the fallback claims everything by construction and never counts).
        return claimsExtension(name) || editorClaims(name)
            ? EntryDiagramState.Potential
            : EntryDiagramState.Unspecified;
    }
}
