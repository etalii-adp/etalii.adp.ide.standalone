using EtAlii.Adp.Backend.History;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Rename the file or folder at <paramref name="FullPath"/> to <paramref name="NewName"/>,
/// leaving it in the same parent folder.
/// </summary>
/// <param name="FullPath">Absolute path of the entry as it stands now.</param>
/// <param name="NewName">The new name only - not a path. A separator in it is rejected.</param>
/// <remarks>
/// This is the commit step of the rename flow: the point at which a name the user has already
/// typed and had validated is actually applied to disk. Its inverse is another
/// <see cref="RenameEntryCommand"/> pointing at the new path and carrying the old name, which
/// is what makes a rename undoable.
/// </remarks>
public sealed record RenameEntryCommand(string FullPath, string NewName) : ICommand;
