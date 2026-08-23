using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Delete the file or folder at <paramref name="FullPath"/> - with everything inside it,
/// when it is a folder.
/// </summary>
/// <param name="FullPath">Absolute path of the entry to remove.</param>
public sealed record DeleteEntryCommand(string FullPath) : ICommand;

/// <summary>
/// Applies a <see cref="DeleteEntryCommand"/> to disk.
/// </summary>
/// <remarks>
/// Reports no inverse, so a delete is deliberately not undoable. Putting an entry back would
/// mean holding its entire contents - a whole folder tree, in the worst case - for as long as
/// it sits on the undo stack, and a half-restored tree is worse than none. The confirmation
/// dialog says as much before anything is removed, so this is what the user was promised.
/// <para>
/// It is still a command rather than an inline delete: it is the one place the removal
/// happens, it re-checks its own preconditions, and it is what a
/// <see cref="CreateDiagramFileCommand"/> hands back as the inverse that undoes a create.
/// </para>
/// </remarks>
public sealed class DeleteEntryCommandHandler : ICommandHandler<DeleteEntryCommand>
{
    private static readonly ILogger _logger = Log.ForContext<DeleteEntryCommandHandler>();

    public Task<CommandResult> ExecuteAsync(
        DeleteEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.FullPath))
        {
            return Task.FromResult(CommandResult.Failure("No entry was given to delete."));
        }

        var path = IoPath.TrimEndingDirectorySeparator(command.FullPath);
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            return Task.FromResult(CommandResult.Failure("The entry no longer exists."));
        }

        try
        {
            if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A recursive delete can fail partway through, so say that the delete did not
            // complete rather than that nothing happened; whatever did get removed arrives
            // as ordinary changes from the watcher, leaving the tree showing real state.
            var remains = isDirectory && Directory.Exists(path);
            var suffix = remains ? " Some of its contents may already have been removed." : "";
            _logger.Warning(exception, "Could not fully delete {Path}", path);
            return Task.FromResult(CommandResult.Failure($"Could not fully delete this item: {exception.Message}{suffix}"));
        }

        _logger.Information("Deleted {Path}", path);
        return Task.FromResult(CommandResult.Success());
    }
}
