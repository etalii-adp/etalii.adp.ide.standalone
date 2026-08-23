using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Delete the file or folder at <paramref name="FullPath"/> - with everything inside it,
/// when it is a folder, and with its document sibling, when it is a diagram's registration
/// file (mindmap-diagram Requirement 2.11).
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

    private readonly IDiagramDefinitionCatalog _catalog;

    public DeleteEntryCommandHandler(IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

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

        // Resolved before the registration file goes: its first line is what names the
        // sibling, and once it is deleted there is nothing left to ask.
        var sibling = isDirectory ? null : DiagramFilePair.SiblingOf(path, _catalog);

        try
        {
            if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
                if (sibling is not null && File.Exists(sibling))
                {
                    File.Delete(sibling);
                }
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
        if (sibling is not null)
        {
            _logger.Information("Deleted {Path} with its registration file", sibling);
        }

        return Task.FromResult(CommandResult.Success());
    }
}
