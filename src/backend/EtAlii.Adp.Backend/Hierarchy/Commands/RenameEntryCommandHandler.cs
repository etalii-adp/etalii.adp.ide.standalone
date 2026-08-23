using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path;

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
public sealed record RenameEntryCommand(
    string FullPath,
    string NewName) : ICommand;

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
    private static readonly ILogger _logger = Log.ForContext<RenameEntryCommandHandler>();

    private readonly IDiagramDefinitionCatalog _catalog;

    public RenameEntryCommandHandler(IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public Task<CommandResult> ExecuteAsync(
        RenameEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.FullPath))
        {
            var result = CommandResult.Failure("No entry was given to rename.");
            return Task.FromResult(result);
        }

        if (ValidateName(command.NewName) is { } nameError)
        {
            var result = CommandResult.Failure(nameError);
            return Task.FromResult(result);
        }

        // A trailing separator would make GetFileName return an empty name, which would leave
        // the inverse command unable to put the entry back.
        var sourcePath = IoPath.TrimEndingDirectorySeparator(command.FullPath);

        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
        {
            var result = CommandResult.Failure("The entry no longer exists.");
            return Task.FromResult(result);
        }

        var parentPath = IoPath.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(parentPath))
        {
            var result = CommandResult.Failure("A root folder cannot be renamed.");
            return Task.FromResult(result);
        }

        var originalName = IoPath.GetFileName(sourcePath);
        var targetPath = IoPath.Combine(parentPath, command.NewName);

        if (string.Equals(targetPath, sourcePath, StringComparison.Ordinal))
        {
            var result = CommandResult.Failure("The new name is the same as the current name.");
            return Task.FromResult(result);
        }

        // On a case-insensitive filesystem the target "already exists" because it *is* the source;
        // changing only capitalisation is still a legitimate rename, so it is let through.
        var isCaseOnlyRename = string.Equals(targetPath, sourcePath, StringComparison.OrdinalIgnoreCase);
        if (!isCaseOnlyRename && (File.Exists(targetPath) || Directory.Exists(targetPath)))
        {
            var result = CommandResult.Failure($"'{command.NewName}' already exists in this folder.");
            return Task.FromResult(result);
        }

        // A diagram's registration file takes its document sibling with it, under the new
        // base name, so the pair stays a pair (mindmap-diagram Requirement 2.10). Resolved
        // before the move: the first line that names the sibling goes with the file.
        var sibling = isDirectory ? null : DiagramFilePair.SiblingOf(sourcePath, _catalog);
        var siblingTarget = sibling is null
            ? null
            : DiagramFilePair.SiblingPathFor(targetPath, IoPath.GetExtension(sibling));
        if (siblingTarget is not null && File.Exists(sibling) && !isCaseOnlyRename &&
            (File.Exists(siblingTarget) || Directory.Exists(siblingTarget)))
        {
            var result = CommandResult.Failure($"'{IoPath.GetFileName(siblingTarget)}' already exists in this folder.");
            return Task.FromResult(result);
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
                if (siblingTarget is not null && File.Exists(sibling))
                {
                    try
                    {
                        File.Move(sibling, siblingTarget);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // One command, one outcome: a sibling that will not follow puts the
                        // registration file back, so an undo never has half a pair to restore.
                        File.Move(targetPath, sourcePath);
                        throw;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Locked by another process, permissions, a path that outgrew the OS limit: all
            // things the user can act on, so they are reported rather than thrown. Logged with
            // the exception, which the message handed to the user does not carry.
            _logger.Warning(exception, "Could not rename {SourcePath} to {TargetPath}", sourcePath, targetPath);
            var result = CommandResult.Failure($"'{originalName}' could not be renamed: {exception.Message}");
            return Task.FromResult(result);
        }

        _logger.Information("Renamed {SourcePath} to {NewName}", sourcePath, command.NewName);
        if (siblingTarget is not null && File.Exists(siblingTarget))
        {
            _logger.Information("Renamed {SiblingPath} with its registration file", siblingTarget);
        }

        // The inverse re-derives the sibling from the renamed registration file, so it
        // restores both names without carrying either.
        return Task.FromResult(CommandResult.Success(new RenameEntryCommand(targetPath, originalName)));
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
