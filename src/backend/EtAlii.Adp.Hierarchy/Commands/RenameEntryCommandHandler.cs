using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

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
    private readonly IHierarchyModelStore? _modelStore;

    /// <param name="catalog">The catalog.</param>
    /// <param name="modelStore">
    /// Optional, and injected in every real deployment (AddHierarchy registers it): where the
    /// stable-id rename notification below is sent. Absent only in a partial test container that
    /// wires up commands without the hierarchy area, where a rename still happens on disk and
    /// simply raises no direct model notification.
    /// </param>
    public RenameEntryCommandHandler(IDiagramDefinitionCatalog catalog, IHierarchyModelStore? modelStore = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _modelStore = modelStore;
    }

    /// <summary>
    /// Tells the hierarchy models a move happened, so a rename keeps the entry's id on every
    /// platform rather than depending on the FileSystemWatcher's event shape (Renamed on
    /// Windows, an uncorrelated Delete+Create on Linux). Guarded on what the disk actually
    /// shows - the source gone, the target present - so a move that did not take this exact
    /// shape (a cascade's secondary files, a case-only rename) is left to the watcher untouched.
    /// Runs for forward, undo and redo alike, since every one executes this handler.
    /// </summary>
    private void NotifyMoved(string source, string target)
    {
        if (_modelStore is null)
        {
            return;
        }

        var sourceGone = !File.Exists(source) && !Directory.Exists(source);
        var targetPresent = File.Exists(target) || Directory.Exists(target);
        if (sourceGone && targetPresent)
        {
            _modelStore.NotifyRenamed(source, target);
        }
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

        // ---- adp-file-nesting Requirement 5.2: a qualified registration renames only its
        // qualifier. Changing the subject portion would silently re-point the diagram at a
        // different file, so that is refused with the way out named.
        if (!isDirectory && DiagramRegistrationName.TryParse(originalName) is { IsQualified: true } qualifiedSource)
        {
            var parsedNew = DiagramRegistrationName.TryParse(command.NewName);
            if (parsedNew is null || parsedNew.IsFolderScoped || !parsedNew.IsQualified ||
                !string.Equals(parsedNew.SubjectBase, qualifiedSource.SubjectBase, StringComparison.Ordinal))
            {
                var result = CommandResult.Failure(
                    $"Only this diagram's qualifier can change: '{qualifiedSource.SubjectBase}.<qualifier>.adp'. " +
                    $"To move every diagram with it, rename '{qualifiedSource.SubjectBase}' itself.");
                return Task.FromResult(result);
            }

            try
            {
                File.Move(sourcePath, targetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(exception, "Could not rename {SourcePath} to {TargetPath}", sourcePath, targetPath);
                var result = CommandResult.Failure($"'{originalName}' could not be renamed: {exception.Message}");
                return Task.FromResult(result);
            }

            _logger.Information("Renamed {SourcePath} to {NewName}", sourcePath, command.NewName);
            NotifyMoved(sourcePath, targetPath);
            return Task.FromResult(CommandResult.Success(new RenameEntryCommand(targetPath, originalName)));
        }

        // ---- adp-file-nesting Requirement 5.1: renaming a SUBJECT renames every registration
        // under it, preserving each qualifier - checked completely before anything moves, so a
        // collision is a refusal rather than a half-renamed set (Requirement 5.3).
        if (!isDirectory && !DiagramFilePair.IsRegistrationFile(sourcePath))
        {
            var setResult = RenameSubjectWithItsRegistrations(sourcePath, targetPath, originalName, command.NewName, isCaseOnlyRename);
            if (setResult is not null)
            {
                if (setResult.IsSuccess)
                {
                    // The subject's own move; the cascade's registration siblings still ride the
                    // watcher (NotifyMoved's disk guard skips any that did not land here).
                    NotifyMoved(sourcePath, targetPath);
                }

                return Task.FromResult(setResult);
            }
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

        // The classic pair rename stays exactly as it was (Requirement 11.1) - but only while
        // it IS the classic pair. With qualified peers over the same subject, carrying the
        // subject off would orphan them (Requirement 6.2's guarantee, in rename form).
        if (sibling is not null && File.Exists(sibling) && DiagramRegistrationSet.Over(parentPath, sibling, _catalog).Any(peer => !string.Equals(peer, sourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            var result = CommandResult.Failure(
                $"Other diagrams also describe '{IoPath.GetFileName(sibling)}'. Rename '{IoPath.GetFileName(sibling)}' itself to move them all together.");
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
        NotifyMoved(sourcePath, targetPath);
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
        // Judged against the portable set, not the platform's: on Linux the platform's own
        // list would let 'report*.txt' through as a name Windows could never check out.
        if (name.IndexOfAny(PortableFileNames.InvalidChars) >= 0)
        {
            return "A name cannot contain any of these characters: " +
                   string.Join(' ', PortableFileNames.InvalidChars.Where(c => !char.IsControl(c)));
        }

        return null;
    }

    /// <summary>
    /// Renames a subject file together with every registration deriving or naming it, or
    /// returns null when the file has no registrations and the ordinary path should run.
    /// Atomic in effect: every target is checked before anything moves, and a mid-set failure
    /// rolls the completed moves back (Requirement 5.3).
    /// </summary>
    private CommandResult? RenameSubjectWithItsRegistrations(string sourcePath, string targetPath, string originalName, string newName, bool isCaseOnlyRename)
    {
        var parentPath = IoPath.GetDirectoryName(sourcePath)!;
        var oldBase = DiagramRegistrationName.TryParse(originalName)?.FullBase ?? IoPath.GetFileNameWithoutExtension(originalName);
        var newBase = IoPath.GetFileNameWithoutExtension(newName);
        var oldExtension = IoPath.GetExtension(originalName);
        if (!string.Equals(IoPath.GetExtension(newName), oldExtension, StringComparison.OrdinalIgnoreCase))
        {
            // Changing the extension changes what derives it; the set logic does not apply.
            return null;
        }

        var registrations = DiagramRegistrationSet.Over(parentPath, sourcePath, _catalog).ToList();
        if (registrations.Count == 0)
        {
            return null;
        }

        // Plan first, move nothing: file moves for name-tied registrations, header rewrites for
        // any registration whose body: header names the subject.
        var moves = new List<(string From, string To)> { (sourcePath, targetPath) };
        var rewrites = new List<(string Path, string OldContent, string NewContent)>();
        foreach (var registration in registrations)
        {
            var registrationName = IoPath.GetFileName(registration);
            var parsed = DiagramRegistrationName.TryParse(registrationName);
            if (parsed is not null && string.Equals(parsed.SubjectBase, oldBase, StringComparison.OrdinalIgnoreCase))
            {
                var renamed = parsed.IsQualified
                    ? $"{newBase}.{parsed.Qualifier}{DiagramFileName.Extension}"
                    : newBase + DiagramFileName.Extension;
                moves.Add((registration, IoPath.Combine(parentPath, renamed)));
            }

            var content = DiagramRegistrationSet.SafeRead(registration);
            if (content is not null)
            {
                var rewritten = DiagramRegistrationSet.RewriteBodyHeaderSegment(content, originalName, newName);
                if (!string.Equals(rewritten, content, StringComparison.Ordinal))
                {
                    rewrites.Add((registration, content, rewritten));
                }
            }
        }

        foreach (var (_, to) in moves.Skip(1))
        {
            if (!isCaseOnlyRename && (File.Exists(to) || Directory.Exists(to)))
            {
                return CommandResult.Failure($"'{IoPath.GetFileName(to)}' already exists in this folder; nothing was renamed.");
            }
        }

        // Header rewrites move with their file when both apply, so path them by final name.
        var finalPathOf = moves.ToDictionary(move => move.From, move => move.To, StringComparer.OrdinalIgnoreCase);

        var completed = new List<(string From, string To)>();
        try
        {
            foreach (var (from, to) in moves)
            {
                File.Move(from, to);
                completed.Add((from, to));
            }

            foreach (var (path, _, newContent) in rewrites)
            {
                AdpFileWriter.Save(finalPathOf.GetValueOrDefault(path, path), newContent);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach (var (from, to) in completed.AsEnumerable().Reverse())
            {
                try
                {
                    File.Move(to, from);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                    _logger.Error(rollback, "Rolling back {To} to {From} failed; the set is inconsistent", to, from);
                }
            }

            _logger.Warning(exception, "Could not rename {SourcePath} and its registrations to {TargetPath}", sourcePath, targetPath);
            return CommandResult.Failure($"'{originalName}' could not be renamed: {exception.Message}");
        }

        _logger.Information("Renamed {SourcePath} to {NewName}, carrying {Count} diagram registration(s)", sourcePath, newName, moves.Count - 1);
        return CommandResult.Success(new RenameEntryCommand(targetPath, originalName));
    }
}
