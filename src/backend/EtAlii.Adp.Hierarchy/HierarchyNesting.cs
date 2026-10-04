namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The nesting decision, taken over one folder's siblings and nothing else (adp-file-nesting
/// Requirement 3): which entries are registrations, which sibling FILE each nests under, and
/// which are orphans. Pure - the caller resolves bodies, this decides placement - so the rule
/// is testable without a filesystem and cannot drift between the list and the watch paths,
/// which both ask here.
/// </summary>
/// <remarks>
/// <para>
/// A subject is always a file, never a folder. That single rule is what a name-based
/// implementation gets wrong: <c>templates.adp</c> beside both <c>templates.yml</c> and a
/// <c>templates/</c> directory must nest under the file. The folder is not a candidate,
/// however well its name matches.
/// </para>
/// <para>
/// A folder-scoped registration - the bare <c>.adp</c> - nests nowhere, because it already
/// sits inside the folder it registers: its subject IS its parent (Requirement 4.2).
/// </para>
/// <para>
/// A registration appears under its subject and nowhere else (Requirement 3.5): the caller
/// relocates the entry, it never duplicates it.
/// </para>
/// </remarks>
public static class HierarchyNesting
{
    /// <summary>
    /// Assigns each registration among <paramref name="siblings"/> a place.
    /// </summary>
    /// <param name="siblings">One folder's entries: name and whether each is a folder.</param>
    /// <param name="resolvedBodyNameOf">
    /// The body FILE NAME a registration resolves in this folder, or null when it resolves
    /// none there - an unknown type, an unreadable file, a type that keeps no body, or a
    /// <c>body:</c> header pointing into another folder. The caller owns that resolution
    /// (it has the catalog and the headers); this function only places the result.
    /// </param>
    public static IReadOnlyList<NestedEntry> Assign(
        IReadOnlyList<(string Name, bool IsFolder)> siblings,
        Func<string, string?> resolvedBodyNameOf)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        ArgumentNullException.ThrowIfNull(resolvedBodyNameOf);

        var siblingFiles = new HashSet<string>(
            siblings.Where(sibling => !sibling.IsFolder).Select(sibling => sibling.Name),
            StringComparer.OrdinalIgnoreCase);

        var results = new List<NestedEntry>();
        foreach ((string name, bool isFolder) in siblings)
        {
            if (isFolder || DiagramRegistrationName.TryParse(name) is not { } registration)
            {
                continue; // not a registration: nothing to place
            }

            if (registration.IsFolderScoped)
            {
                // The bare .adp already sits inside its subject; nesting it anywhere else
                // would move it OUT of the folder it registers.
                results.Add(new NestedEntry(name, SubjectName: null, IsOrphan: false));
                continue;
            }

            var body = resolvedBodyNameOf(name);
            if (body is null)
            {
                // Nothing resolved: current, un-nested behaviour, and not an error
                // (Requirement 8.4's stance carried into placement).
                results.Add(new NestedEntry(name, SubjectName: null, IsOrphan: false));
                continue;
            }

            results.Add(siblingFiles.TryGetValue(body, out var subject)
                // The subject file exists here: nest under it - and only ever under a file.
                ? new NestedEntry(name, subject, IsOrphan: false)
                // A subject was resolved but is missing: the registration stays visible where
                // it sits, marked, so the broken pair can be seen and fixed (Requirement 8.1).
                : new NestedEntry(name, SubjectName: null, IsOrphan: true));
        }

        return results;
    }

    /// <summary>
    /// The stable order of one subject's registrations (Requirement 3.4): the unqualified form
    /// first - it is the default Requirement 7.2 activates - then the qualified ones by
    /// qualifier, ordinal, so the order does not vary by locale.
    /// </summary>
    public static int CompareRegistrations(string nameA, string nameB)
    {
        var a = DiagramRegistrationName.TryParse(nameA);
        var b = DiagramRegistrationName.TryParse(nameB);
        if (a is null || b is null)
        {
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }

        var byQualification = a.IsQualified.CompareTo(b.IsQualified);
        return byQualification != 0
            ? byQualification
            : string.Compare(a.Qualifier, b.Qualifier, StringComparison.Ordinal);
    }
}
