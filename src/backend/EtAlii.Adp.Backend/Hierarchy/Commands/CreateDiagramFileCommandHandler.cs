using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Create a new diagram file called <paramref name="FileName"/> in <paramref name="Folder"/>,
/// holding <paramref name="FirstLine"/> as its only line.
/// </summary>
/// <param name="Folder">Absolute path of the folder the file belongs in.</param>
/// <param name="FileName">The file's name, extension included.</param>
/// <param name="FirstLine">The single line to write - the chosen diagram type's MIME type.</param>
/// <remarks>
/// The command carries no diagram type of its own: what a diagram file starts as is decided
/// where the user chose it, so this stays a plain "write these bytes under this name" that
/// undo and redo can replay without re-resolving anything.
/// </remarks>
public sealed record CreateDiagramFileCommand(
    string Folder,
    string FileName,
    string FirstLine) : ICommand;

/// <summary>
/// Applies a <see cref="CreateDiagramFileCommand"/> to disk, reporting the delete that
/// removes the new file again as its inverse.
/// </summary>
/// <remarks>
/// Re-checks the folder rather than trusting the caller: a redo runs long after the choice
/// was made, and the folder may have gone in the meantime.
/// </remarks>
public sealed class CreateDiagramFileCommandHandler : ICommandHandler<CreateDiagramFileCommand>
{
    // No logger of its own: AdpFileWriter already reports what it wrote and what it could not.

    public Task<CommandResult> ExecuteAsync(
        CreateDiagramFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(command.Folder))
        {
            return Task.FromResult(CommandResult.Failure("The folder no longer exists."));
        }

        return Task.FromResult(AdpFileWriter.Create(command.Folder, command.FileName, command.FirstLine) switch
        {
            // The inverse is the delete of exactly what was just written, which is what makes
            // an accidental Add one Ctrl+Z away.
            AdpFileWriteResult.Created created => CommandResult.Success(new DeleteEntryCommand(created.FullPath)),

            // Someone got there in the moment between judging the name and using it. The user
            // picks another one; nothing is overwritten and no name is invented for them.
            AdpFileWriteResult.NameTaken =>
                CommandResult.Failure($"An item named '{command.FileName}' already exists in this folder."),

            AdpFileWriteResult.Failed failed => CommandResult.Failure($"Could not create the diagram: {failed.Message}"),

            _ => CommandResult.Failure("Could not create the diagram."),
        });
    }

    /// <summary>Where <paramref name="command"/> would put its file, without writing anything.</summary>
    public static string DestinationOf(CreateDiagramFileCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return IoPath.Combine(command.Folder, command.FileName);
    }
}
