using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

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
    private readonly ToolRouter? _tools;
    private readonly IFileWriter _writer;

    /// <param name="catalog">The diagram types, for a registration's body file and a subject's registrations.</param>
    /// <param name="writer">What deletes a file, taking the turn a save of it takes.</param>
    /// <param name="tools">
    /// Which tool serves a file, or null in a host without the designer and editor families. A
    /// designer's document is two files as a diagram with a body file is, and neither is left
    /// behind by the other.
    /// </param>
    public DeleteEntryCommandHandler(IDiagramDefinitionCatalog catalog, IFileWriter writer, ToolRouter? tools = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(writer);
        _catalog = catalog;
        _writer = writer;
        _tools = tools;
    }

    public async Task<CommandResult> ExecuteAsync(
        DeleteEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.FullPath))
        {
            return CommandResult.Failure("No entry was given to delete.");
        }

        var path = IoPath.TrimEndingDirectorySeparator(command.FullPath);
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            return CommandResult.Failure("The entry no longer exists.");
        }

        var (sibling, cascade) = isDirectory ? (null, []) : CompanionsOf(path);
        if (cascade.Count > 0)
        {
            _logger.Information("Deleting {Path} and the {Count} diagram registration(s) over it", path, cascade.Count);
        }

        try
        {
            if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                await DeleteFileAsync(path, sibling, cascade, cancellationToken);
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
            return CommandResult.Failure($"Could not fully delete this item: {exception.Message}{suffix}");
        }

        _logger.Information("Deleted {Path}", path);
        if (sibling is not null)
        {
            _logger.Information("Deleted {Path} with its registration file", sibling);
        }

        return CommandResult.Success();
    }

    /// <summary>
    /// What goes with a file: its document sibling, and the registrations a subject's delete
    /// cascades to. Resolved before the registration file goes: its first line is what names the
    /// sibling, and once it is deleted there is nothing left to ask. Whose document the file is, a
    /// designer's or otherwise a diagram's, is the tool router's answer.
    /// </summary>
    private (string? Sibling, List<string> Cascade) CompanionsOf(string path)
    {
        var designer = _tools?.Route(path) as DesignerRouted;
        var isRegistration = DiagramFilePair.IsRegistrationFile(path);
        var sibling = designer is null ? DiagramFilePair.SiblingOf(path, _catalog)
            : isRegistration ? designer.BodyPath
            : null;

        // A SUBJECT's delete cascades to its registrations (adp-file-nesting Requirement 6.1):
        // a registration pointing at nothing serves nobody, and the confirmation dialog said
        // the count before this ran. Deleting one REGISTRATION cascades to nothing beyond its
        // owned sibling - a body other registrations still reference is never owned (6.2).
        List<string> cascade = !isRegistration
            ? DiagramRegistrationSet.Over(IoPath.GetDirectoryName(path)!, path, _catalog).ToList()
            : [];
        if (designer is not null && !isRegistration)
        {
            // The body of a designer's document: its registration names nothing once it is gone.
            cascade.Add(designer.RegistrationPath);
        }

        return (sibling, cascade);
    }

    private async Task DeleteFileAsync(string path, string? sibling, List<string> cascade, CancellationToken cancellationToken)
    {
        await _writer.DeleteAsync(path, cancellationToken);
        if (sibling is not null && File.Exists(sibling))
        {
            await _writer.DeleteAsync(sibling, cancellationToken);
        }

        foreach (var registration in cascade)
        {
            await _writer.DeleteAsync(registration, cancellationToken);
        }
    }
}
