using EtAlii.Adp.Common;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The single judgement of "is this a usable name for an entry in this folder", shared by
/// every action that names something: renaming an existing entry and creating a new one.
/// Keeping it in one place is what stops the two from drifting into different ideas of a
/// valid name - and what makes the rejection the dialog shows while typing identical to the
/// one the commit refuses with.
/// </summary>
public static class EntryNameRules
{
    /// <summary>
    /// Judges <paramref name="name"/> as an entry name directly inside
    /// <paramref name="parentFolder"/>. Pass <paramref name="currentName"/> when an existing
    /// entry is being renamed: the name must then also differ from it, while a change of
    /// casing alone is allowed even though the entry appears to collide with itself on a
    /// case-insensitive filesystem.
    /// </summary>
    public static ContextValidationResult Validate(string name, string parentFolder, string? currentName = null)
    {
        var newName = name.Trim();
        if (newName.Length == 0)
        {
            return ContextValidationResult.Rejected("Enter a name.");
        }

        // A name is a name, not a location - so anything that could reach outside this very
        // folder is refused outright rather than sanitised: the user can see it and fix it.
        // Judged against the portable set, not the platform's: the platform's own idea of an
        // invalid character let `bad*name.txt` through on Linux while this message promised
        // otherwise, and the file would never check out on Windows anyway.
        if (newName is "." or ".." || newName.IndexOfAny(PortableFileNames.InvalidChars) >= 0)
        {
            return ContextValidationResult.Rejected("A name cannot contain a path or any of \\ / : * ? \" < > |");
        }

        if (currentName is not null && string.Equals(newName, currentName, StringComparison.Ordinal))
        {
            return ContextValidationResult.Rejected("Enter a name that differs from the current one.");
        }

        string destination;
        try
        {
            destination = IoPath.GetFullPath(IoPath.Combine(parentFolder, newName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ContextValidationResult.Rejected("That name cannot be used on this system.");
        }

        // Belt and braces after the character check above: whatever the name turned out to
        // mean on this filesystem, it must still land directly in this folder.
        var destinationParent = IoPath.GetDirectoryName(destination);
        if (destinationParent is null || !string.Equals(destinationParent, parentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return ContextValidationResult.Rejected("A name cannot contain a path.");
        }

        // On a case-insensitive filesystem, changing only the casing has the entry colliding
        // with itself; that is a legitimate rename, so let the move handle it.
        var isSameEntry = currentName is not null &&
            string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase);
        if (!isSameEntry && (File.Exists(destination) || Directory.Exists(destination)))
        {
            return ContextValidationResult.Rejected($"An item named '{newName}' already exists in this folder.");
        }

        return ContextValidationResult.Accepted;
    }
}
