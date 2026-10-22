using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Hierarchy;

/// <summary>Applies a <see cref="CreateDocumentBodyCommand"/>, reporting the delete of the new file as its inverse.</summary>
/// <remarks>Re-checks the folder rather than trusting the caller, as <see cref="CreateDiagramFileCommandHandler"/> does: a redo runs long after the choice was made.</remarks>
public sealed class CreateDocumentBodyCommandHandler : ICommandHandler<CreateDocumentBodyCommand>
{
    public Task<CommandResult> ExecuteAsync(CreateDocumentBodyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(command.Folder))
        {
            return Task.FromResult(CommandResult.Failure("The folder no longer exists."));
        }

        return Task.FromResult(AdpFileWriter.CreateAll(command.Folder, [(command.FileName, command.Content)]) switch
        {
            AdpFileCreated created => CommandResult.Success(new DeleteEntryCommand(created.FullPath)),
            AdpFileNameTaken => CommandResult.Failure($"An item named '{command.FileName}' already exists in this folder."),
            AdpFileWriteFailed failed => CommandResult.Failure($"Could not create the document: {failed.Message}"),
            _ => CommandResult.Failure("Could not create the document."),
        });
    }
}
