using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Create a new diagram file called <paramref name="FileName"/> in <paramref name="Folder"/>,
/// holding <paramref name="FirstLine"/> as its only line - and, for a type that keeps its
/// body in a sibling file, that sibling beside it.
/// </summary>
/// <param name="Folder">Absolute path of the folder the file belongs in.</param>
/// <param name="FileName">The registration file's name, extension included.</param>
/// <param name="FirstLine">The single line to write - the chosen diagram type's MIME type.</param>
/// <param name="SiblingFileName">The body file's name, or empty when the type keeps no sibling.</param>
/// <param name="SiblingContent">The body file's complete initial content; ignored when there is no sibling.</param>
/// <remarks>
/// The command carries no diagram type of its own: what a diagram file starts as, and what
/// its body starts as, is decided where the user chose it - the provider resolves the type's
/// <c>IDiagramDocumentFactory</c> and puts the result here. That keeps this a plain "write
/// these bytes under these names" that undo and redo can replay without re-resolving
/// anything, and it keeps the handler free of any diagram-type lookup.
/// </remarks>
public sealed record CreateDiagramFileCommand(
    string Folder,
    string FileName,
    string FirstLine,
    string SiblingFileName = "",
    string SiblingContent = "") : ICommand
{
    public bool HasSibling => SiblingFileName.Length > 0;
}

/// <summary>
/// Applies a <see cref="CreateDiagramFileCommand"/> to disk, reporting the delete that
/// removes the new file - and its sibling - again as its inverse.
/// </summary>
/// <remarks>
/// Re-checks the folder rather than trusting the caller: a redo runs long after the choice
/// was made, and the folder may have gone in the meantime. Both files are created or neither
/// is (mindmap-diagram Requirement 1.6); <see cref="AdpFileWriter.CreateAll"/> owns that.
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

        var files = new List<(string FileName, string Content)> { (command.FileName, command.FirstLine + "\n") };
        if (command.HasSibling)
        {
            files.Add((command.SiblingFileName, command.SiblingContent));
        }

        return Task.FromResult(AdpFileWriter.CreateAll(command.Folder, files) switch
        {
            // The inverse is the delete of exactly what was just written. The delete command
            // removes a registration file's sibling with it, so one inverse covers both files
            // and an accidental Add is one Ctrl+Z away.
            AdpFileCreated created => CommandResult.Success(new DeleteEntryCommand(created.FullPath)),

            // Someone got there in the moment between judging the name and using it. The user
            // picks another one; nothing is overwritten and no name is invented for them.
            AdpFileNameTaken =>
                CommandResult.Failure($"An item named '{command.FileName}' already exists in this folder."),

            AdpFileWriteFailed failed => CommandResult.Failure($"Could not create the diagram: {failed.Message}"),

            _ => CommandResult.Failure("Could not create the diagram."),
        });
    }

    /// <summary>Where <paramref name="command"/> would put its registration file, without writing anything.</summary>
    public static string DestinationOf(CreateDiagramFileCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return IoPath.Combine(command.Folder, command.FileName);
    }
}
