using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
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
/// <param name="Root">
/// The project the entry is in, when the rename is made in one: what names the entry elsewhere in
/// that project follows it (<see cref="IEntryRenameFollower"/>). The default, nothing follows.
/// </param>
/// <remarks>
/// This is the commit step of the rename flow: the point at which a name the user has already
/// typed and had validated is actually applied to disk. Its inverse is another
/// <see cref="RenameEntryCommand"/> pointing at the new path and carrying the old name, which
/// is what makes a rename undoable.
/// </remarks>
public sealed record RenameEntryCommand(
    string FullPath,
    string NewName,
    ProjectRoot Root = default) : ICommand;

/// <summary>
/// Applies a <see cref="RenameEntryCommand"/> to disk.
/// </summary>
/// <remarks>
/// The handler re-checks everything it depends on rather than trusting the validation the user
/// already passed in the rename dialog. Two reasons: the folder can change between the moment a
/// name was validated and the moment Rename was pressed, and undo/redo dispatch this handler
/// again later, when the earlier check is long stale.
/// <para>
/// It deliberately does not notify anyone of the rename. The connection's file events observe the
/// move and the existing <c>EntryRenamed</c> change flows out over <c>WatchHierarchy</c>, so an
/// undo reaches connected clients by exactly the same route as the original rename.
/// </para>
/// </remarks>
public sealed class RenameEntryCommandHandler : ICommandHandler<RenameEntryCommand>
{
    private static readonly ILogger _logger = Log.ForContext<RenameEntryCommandHandler>();

    private readonly IDiagramDefinitionCatalog _catalog;
    private readonly IHierarchyModelStore? _modelStore;
    private readonly IReadOnlyList<IEntryRenameFollower> _followers;
    private readonly IFileWriter _writer;

    /// <param name="catalog">The catalog.</param>
    /// <param name="writer">What rewrites a registration whose body header names the renamed file.</param>
    /// <param name="modelStore">
    /// Optional, and injected in every real deployment (AddHierarchy registers it): where the
    /// stable-id rename notification below is sent. Absent only in a partial test container that
    /// wires up commands without the hierarchy area, where a rename still happens on disk and
    /// simply raises no direct model notification.
    /// </param>
    /// <param name="followers">What the tool types registered to follow a rename; none in a host without such a module.</param>
    public RenameEntryCommandHandler(IDiagramDefinitionCatalog catalog, IFileWriter writer, IHierarchyModelStore? modelStore = null, IEnumerable<IEntryRenameFollower>? followers = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(writer);
        _catalog = catalog;
        _writer = writer;
        _modelStore = modelStore;
        _followers = [.. followers ?? []];
    }

    /// <summary>
    /// Tells the hierarchy models a move happened, so a rename keeps the entry's id on every
    /// platform rather than depending on the FileSystemWatcher's event shape (Renamed on
    /// Windows, an uncorrelated Delete+Create on Linux). Guarded on what the disk actually
    /// shows - the source gone, the target present - so a move that did not take this exact
    /// shape (a cascade's secondary files, a case-only rename) is left to the watcher untouched.
    /// Runs for forward, undo and redo alike, since every one executes this handler.
    /// </summary>
    /// <returns>Whether the models were told.</returns>
    private bool NotifyMoved(string source, string target)
    {
        if (_modelStore is null)
        {
            return false;
        }

        var sourceGone = !File.Exists(source) && !Directory.Exists(source);
        var targetPresent = File.Exists(target) || Directory.Exists(target);
        if (sourceGone && targetPresent)
        {
            _modelStore.NotifyRenamed(source, target);
            return true;
        }

        return false;
    }

    public async Task<CommandResult> ExecuteAsync(
        RenameEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.FullPath))
        {
            var result = CommandResult.Failure("No entry was given to rename.");
            return result;
        }

        // A trailing separator would make GetFileName return an empty name, which would leave
        // the inverse command unable to put the entry back.
        var sourcePath = IoPath.TrimEndingDirectorySeparator(command.FullPath);

        // The rules and the sentences the dialog shows while the user types (behaviour change B-07,
        // ruled 2026-10-16): a rename refuses exactly what the dialog refuses, in the same words,
        // and renames to the name the rules judged, which is the one without surrounding space.
        var folder = IoPath.GetDirectoryName(sourcePath);
        if (!string.IsNullOrEmpty(folder) &&
            EntryNameRules.Validate(command.NewName, folder, IoPath.GetFileName(sourcePath)) is { Valid: false } refused)
        {
            var result = CommandResult.Failure(refused.Reason);
            return result;
        }

        var newName = command.NewName.Trim();

        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
        {
            var result = CommandResult.Failure("The entry no longer exists.");
            return result;
        }

        var parentPath = IoPath.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(parentPath))
        {
            var result = CommandResult.Failure("A root folder cannot be renamed.");
            return result;
        }

        var rename = new Rename(sourcePath, IoPath.Combine(parentPath, newName), IoPath.GetFileName(sourcePath), newName, isDirectory);
        var moved = await MoveAndNotifyAsync(rename, cancellationToken);
        if (moved is { IsSuccess: true, Inverse: RenameEntryCommand inverse } && command.Root.FullPath.Length > 0)
        {
            // What names the entry elsewhere in the project follows it, and follows it back: the
            // inverse is a rename in the same project, so an undo runs the followers the other way.
            await FollowAsync(command.Root, sourcePath, rename.TargetPath);
            moved = CommandResult.Success(inverse with { Root = command.Root });
        }

        return moved;
    }

    /// <summary>One rename, as the handler has checked it: where the entry is, where it goes, and its names before and after.</summary>
    private sealed record Rename(string SourcePath, string TargetPath, string OriginalName, string NewName, bool IsDirectory)
    {
        public string ParentPath => IoPath.GetDirectoryName(SourcePath)!;

        // On a case-insensitive filesystem the target "already exists" because it *is* the source;
        // changing only capitalisation is still a legitimate rename, which the rules let through.
        public bool IsCaseOnly => string.Equals(TargetPath, SourcePath, StringComparison.OrdinalIgnoreCase);

        /// <summary>A rename refused because the entry could not be moved, with the reason the disk gave.</summary>
        public CommandResult CouldNotRename(Exception exception) =>
            CommandResult.Failure($"'{OriginalName}' could not be renamed: {exception.Message}");

        /// <summary>Success, carrying the rename that puts the entry back.</summary>
        public CommandResult Renamed() => CommandResult.Success(new RenameEntryCommand(TargetPath, OriginalName));
    }

    /// <summary>The move, with the hierarchy models told of it as <see cref="NotifyMoved"/> says.</summary>
    private async Task<CommandResult> MoveAndNotifyAsync(Rename rename, CancellationToken cancellationToken)
    {
        // Announced before the move rather than after it: the watcher thread can see the move
        // before NotifyMoved runs, and on Linux an echo the models do not yet expect lands as a
        // Remove+Create that loses the entry's id. A case-only rename is left to the watcher
        // entirely, as NotifyMoved's disk guard leaves it, so it is not announced either.
        var announced = _modelStore is not null && !rename.IsCaseOnly;
        if (announced)
        {
            _modelStore!.ExpectRename(rename.SourcePath, rename.TargetPath);
        }

        var moved = await MoveAsync(rename, cancellationToken);
        var notified = moved.IsSuccess && NotifyMoved(rename.SourcePath, rename.TargetPath);
        if (announced && !notified)
        {
            _modelStore!.WithdrawRename(rename.SourcePath, rename.TargetPath);
        }

        return moved;
    }

    /// <summary>Tells each follower of the move. One that fails is logged and does not undo a rename that happened.</summary>
    /// <remarks>Not cancelled: the rename has happened, and a follow stopped halfway would leave relations naming where it was.</remarks>
    private async Task FollowAsync(ProjectRoot root, string sourcePath, string targetPath)
    {
        foreach (var follower in _followers)
        {
            try
            {
                await follower.RenamedAsync(root, sourcePath, targetPath, CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(exception, "{Follower} could not follow the rename of {SourcePath} to {TargetPath}", follower.GetType().Name, sourcePath, targetPath);
            }
        }
    }

    /// <summary>The move itself, for every shape a rename takes; the models are told by the caller.</summary>
    private async Task<CommandResult> MoveAsync(Rename rename, CancellationToken cancellationToken)
    {
        // ---- adp-file-nesting Requirement 5.2: a qualified registration renames only its
        // qualifier.
        if (!rename.IsDirectory && DiagramRegistrationName.TryParse(rename.OriginalName) is { IsQualified: true } qualifiedSource)
        {
            return RenameQualifier(rename, qualifiedSource);
        }

        // ---- adp-file-nesting Requirement 5.1: renaming a SUBJECT renames every registration
        // under it, preserving each qualifier - checked completely before anything moves, so a
        // collision is a refusal rather than a half-renamed set (Requirement 5.3).
        if (!rename.IsDirectory && !DiagramFilePair.IsRegistrationFile(rename.SourcePath))
        {
            // On success the caller tells the models of the subject's own move; the cascade's
            // registration siblings still ride the watcher.
            var setResult = await RenameSubjectWithItsRegistrationsAsync(rename, cancellationToken);
            if (setResult is not null)
            {
                return setResult;
            }
        }

        // A diagram's registration file takes its document sibling with it, under the new
        // base name, so the pair stays a pair (mindmap-diagram Requirement 2.10). Resolved
        // before the move: the first line that names the sibling goes with the file.
        var sibling = rename.IsDirectory ? null : DiagramFilePair.SiblingOf(rename.SourcePath, _catalog);
        var siblingTarget = sibling is null
            ? null
            : DiagramFilePair.SiblingPathFor(rename.TargetPath, IoPath.GetExtension(sibling));
        if (siblingTarget is not null && File.Exists(sibling) && !rename.IsCaseOnly &&
            (File.Exists(siblingTarget) || Directory.Exists(siblingTarget)))
        {
            return CommandResult.Failure($"'{IoPath.GetFileName(siblingTarget)}' already exists in this folder.");
        }

        // The classic pair rename stays exactly as it was (Requirement 11.1) - but only while
        // it IS the classic pair. With qualified peers over the same subject, carrying the
        // subject off would orphan them (Requirement 6.2's guarantee, in rename form).
        if (sibling is not null && File.Exists(sibling) && DiagramRegistrationSet.Over(rename.ParentPath, sibling, _catalog).Any(peer => !string.Equals(peer, rename.SourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            return CommandResult.Failure(
                $"Other diagrams also describe '{IoPath.GetFileName(sibling)}'. Rename '{IoPath.GetFileName(sibling)}' itself to move them all together.");
        }

        return MoveWithSibling(rename, sibling, siblingTarget);
    }

    /// <summary>
    /// Renames a qualified registration's qualifier. Changing the subject portion would silently
    /// re-point the diagram at a different file, so that is refused with the way out named.
    /// </summary>
    private static CommandResult RenameQualifier(Rename rename, DiagramRegistrationName qualifiedSource)
    {
        var parsedNew = DiagramRegistrationName.TryParse(rename.NewName);
        if (parsedNew is null || parsedNew.IsFolderScoped || !parsedNew.IsQualified ||
            !string.Equals(parsedNew.SubjectBase, qualifiedSource.SubjectBase, StringComparison.Ordinal))
        {
            return CommandResult.Failure(
                $"Only this diagram's qualifier can change: '{qualifiedSource.SubjectBase}.<qualifier>.adp'. " +
                $"To move every diagram with it, rename '{qualifiedSource.SubjectBase}' itself.");
        }

        try
        {
            File.Move(rename.SourcePath, rename.TargetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not rename {SourcePath} to {TargetPath}", rename.SourcePath, rename.TargetPath);
            return rename.CouldNotRename(exception);
        }

        _logger.Information("Renamed {SourcePath} to {NewName}", rename.SourcePath, rename.NewName);
        return rename.Renamed();
    }

    /// <summary>Moves the entry, and a registration's document sibling with it when it has one.</summary>
    private static CommandResult MoveWithSibling(Rename rename, string? sibling, string? siblingTarget)
    {
        try
        {
            if (rename.IsDirectory)
            {
                Directory.Move(rename.SourcePath, rename.TargetPath);
            }
            else
            {
                File.Move(rename.SourcePath, rename.TargetPath);
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
                        File.Move(rename.TargetPath, rename.SourcePath);
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
            _logger.Warning(exception, "Could not rename {SourcePath} to {TargetPath}", rename.SourcePath, rename.TargetPath);
            return rename.CouldNotRename(exception);
        }

        _logger.Information("Renamed {SourcePath} to {NewName}", rename.SourcePath, rename.NewName);
        if (siblingTarget is not null && File.Exists(siblingTarget))
        {
            _logger.Information("Renamed {SiblingPath} with its registration file", siblingTarget);
        }

        // The inverse re-derives the sibling from the renamed registration file, so it
        // restores both names without carrying either.
        return rename.Renamed();
    }

    /// <summary>
    /// Renames a subject file together with every registration deriving or naming it, or
    /// returns null when the file has no registrations and the ordinary path should run.
    /// Atomic in effect: every target is checked before anything moves, and a mid-set failure
    /// rolls the completed moves back (Requirement 5.3).
    /// </summary>
    private async Task<CommandResult?> RenameSubjectWithItsRegistrationsAsync(Rename rename, CancellationToken cancellationToken)
    {
        if (!string.Equals(IoPath.GetExtension(rename.NewName), IoPath.GetExtension(rename.OriginalName), StringComparison.OrdinalIgnoreCase))
        {
            // Changing the extension changes what derives it; the set logic does not apply.
            return null;
        }

        var registrations = DiagramRegistrationSet.Over(rename.ParentPath, rename.SourcePath, _catalog).ToList();
        if (registrations.Count == 0)
        {
            return null;
        }

        var (moves, rewrites) = PlanSet(rename, registrations);
        foreach ((var _, string to) in moves.Skip(1))
        {
            if (!rename.IsCaseOnly && (File.Exists(to) || Directory.Exists(to)))
            {
                return CommandResult.Failure($"'{IoPath.GetFileName(to)}' already exists in this folder; nothing was renamed.");
            }
        }

        return await ApplySetAsync(rename, moves, rewrites, cancellationToken);
    }

    /// <summary>
    /// Plans a subject's set and moves nothing: file moves for name-tied registrations, the
    /// subject's own first, and header rewrites for any registration whose body: header names the
    /// subject.
    /// </summary>
    private static (List<(string From, string To)> Moves, List<(string Path, string OldContent, string NewContent)> Rewrites) PlanSet(Rename rename, IReadOnlyList<string> registrations)
    {
        var oldBase = DiagramRegistrationName.TryParse(rename.OriginalName)?.FullBase ?? IoPath.GetFileNameWithoutExtension(rename.OriginalName);
        var newBase = IoPath.GetFileNameWithoutExtension(rename.NewName);
        var moves = new List<(string From, string To)> { (rename.SourcePath, rename.TargetPath) };
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
                moves.Add((registration, IoPath.Combine(rename.ParentPath, renamed)));
            }

            if (AdpRegistration.Read(registration) is { } read)
            {
                var rewritten = read.WithBodyFileName(rename.OriginalName, rename.NewName);
                if (!string.Equals(rewritten, read.Text, StringComparison.Ordinal))
                {
                    rewrites.Add((registration, read.Text, rewritten));
                }
            }
        }

        return (moves, rewrites);
    }

    /// <summary>Carries out a planned set; a failure part-way rolls the completed moves back.</summary>
    private async Task<CommandResult> ApplySetAsync(
        Rename rename,
        List<(string From, string To)> moves,
        List<(string Path, string OldContent, string NewContent)> rewrites,
        CancellationToken cancellationToken)
    {
        // Header rewrites move with their file when both apply, so path them by final name.
        var finalPathOf = moves.ToDictionary(move => move.From, move => move.To, StringComparer.OrdinalIgnoreCase);

        var completed = new List<(string From, string To)>();
        try
        {
            foreach ((string from, string to) in moves)
            {
                File.Move(from, to);
                completed.Add((from, to));
            }

            foreach ((string path, var _, string newContent) in rewrites)
            {
                await _writer.SaveAsync(finalPathOf.GetValueOrDefault(path, path), newContent, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach ((string from, string to) in completed.AsEnumerable().Reverse())
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

            _logger.Warning(exception, "Could not rename {SourcePath} and its registrations to {TargetPath}", rename.SourcePath, rename.TargetPath);
            return rename.CouldNotRename(exception);
        }

        _logger.Information("Renamed {SourcePath} to {NewName}, carrying {Count} diagram registration(s)", rename.SourcePath, rename.NewName, moves.Count - 1);
        return rename.Renamed();
    }
}
