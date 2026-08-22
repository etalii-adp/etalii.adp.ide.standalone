using EtAlii.Adp.Backend.History;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Applies a <see cref="RenameEntryCommand"/> to disk.
/// </summary>
/// <remarks>
/// The handler re-checks everything it depends on rather than trusting the validation the user
/// already passed in the rename dialog. Two reasons: the folder can change between the moment a
/// name was validated and the moment Rename was pressed, and undo/redo dispatch this handler
/// again later, when the earlier check is long stale.
/// <para>
/// It deliberately does not notify anyone of the rename. <c>RootFolderWatcher</c> observes the
/// move and the existing <c>EntryRenamed</c> change flows out over <c>WatchHierarchy</c>, so an
/// undo reaches connected clients by exactly the same route as the original rename.
/// </para>
/// </remarks>
public sealed class RenameEntryCommandHandler : ICommandHandler<RenameEntryCommand>
{
    public Task<CommandResult> ExecuteAsync(
        RenameEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Execute(command));
    }

    private static CommandResult Execute(RenameEntryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FullPath))
        {
            return CommandResult.Failure("No entry was given to rename.");
        }

        if (ValidateName(command.NewName) is { } nameError)
        {
            return CommandResult.Failure(nameError);
        }

        // A trailing separator would make GetFileName return an empty name, which would leave
        // the inverse command unable to put the entry back.
        var sourcePath = IoPath.TrimEndingDirectorySeparator(command.FullPath);

        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
        {
            return CommandResult.Failure("The entry no longer exists.");
        }

        var parentPath = IoPath.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(parentPath))
        {
            return CommandResult.Failure("A root folder cannot be renamed.");
        }

        var originalName = IoPath.GetFileName(sourcePath);
        var targetPath = IoPath.Combine(parentPath, command.NewName);

        if (string.Equals(targetPath, sourcePath, StringComparison.Ordinal))
        {
            return CommandResult.Failure("The new name is the same as the current name.");
        }

        // On a case-insensitive filesystem the target "already exists" because it *is* the source;
        // changing only capitalisation is still a legitimate rename, so it is let through.
        var isCaseOnlyRename = string.Equals(targetPath, sourcePath, StringComparison.OrdinalIgnoreCase);
        if (!isCaseOnlyRename && (File.Exists(targetPath) || Directory.Exists(targetPath)))
        {
            return CommandResult.Failure($"'{command.NewName}' already exists in this folder.");
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Locked by another process, permissions, a path that outgrew the OS limit: all
            // things the user can act on, so they are reported rather than thrown.
            return CommandResult.Failure($"'{originalName}' could not be renamed: {exception.Message}");
        }

        return CommandResult.Success(new RenameEntryCommand(targetPath, originalName));
    }

    /// <returns>The reason the name is unusable, or <c>null</c> when it is fine.</returns>
    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A name is required.";
        }

        if (name is "." or "..")
        {
            return $"'{name}' is not a usable name.";
        }

        // Checked explicitly rather than relying on GetInvalidFileNameChars, which on Unix
        // covers only '/' and would let a Windows-style '\' through as a legal filename char.
        if (name.Contains(IoPath.DirectorySeparatorChar) ||
            name.Contains(IoPath.AltDirectorySeparatorChar) ||
            name.Contains('\\') ||
            name.Contains('/'))
        {
            return "A name cannot contain a path separator.";
        }

        // Kept as a guard clause to match the checks above. This trips IDE0046 at info level;
        // collapsing it would only push the same suggestion onto the previous clause and turn a
        // readable chain into nested ternaries (PathTruncator.cs carries the same info already).
        if (name.IndexOfAny(IoPath.GetInvalidFileNameChars()) >= 0)
        {
            return "A name cannot contain any of these characters: " +
                   string.Join(' ', IoPath.GetInvalidFileNameChars().Where(c => !char.IsControl(c)));
        }

        return null;
    }
}
