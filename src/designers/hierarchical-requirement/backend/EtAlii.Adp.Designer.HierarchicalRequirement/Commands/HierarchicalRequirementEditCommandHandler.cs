using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Carries out a <see cref="HierarchicalRequirementEditCommand"/>: checks that the body still holds the bytes the edit
/// was planned against, makes the edit through the FBL library, and saves the result with the central writer. Its
/// inverse is a <see cref="RestoreRequirementFilesCommand"/> of the body's bytes from before, so an undo gives them back
/// exactly, and refuses once another program has written the file since.
/// </summary>
internal sealed class HierarchicalRequirementEditCommandHandler(IHierarchicalRequirementDocumentStore documents, IFileWriter writer) : ICommandHandler<HierarchicalRequirementEditCommand>
{
    public async Task<CommandResult> ExecuteAsync(HierarchicalRequirementEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var name = Path.GetFileName(command.BodyPath);
        byte[] before;
        try
        {
            before = SharedDocumentReader.ReadAllBytes(command.BodyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"{name} could not be read: {exception.Message}");
        }

        if (!before.AsSpan().SequenceEqual(command.Expected))
        {
            return CommandResult.Failure(HierarchicalRequirementGestures.Drifted);
        }

        (byte[]? after, string refusal) = command.Operation switch
        {
            _ when TextOperations.Handles(command.Operation) => TextOperations.Apply(before, name, command.Operation, command.TargetId, command.Params.GetValueOrDefault(TextOperations.AttributeOf(command.Operation), "")),
            _ when StructureOperations.Handles(command.Operation) => StructureOperations.Apply(before, name, command.Operation, command.TargetId, command.Params),
            _ => (null, $"'{command.Operation}' cannot be done here yet."),
        };
        if (after is null)
        {
            return CommandResult.Failure(refusal);
        }

        if (after.AsSpan().SequenceEqual(before))
        {
            // Nothing to write and nothing to undo.
            return CommandResult.Success();
        }

        documents.WillWrite(command.BodyPath, after);
        try
        {
            await writer.SaveAsync(command.BodyPath, after, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"{name} could not be written: {exception.Message}");
        }

        // Not with the request's token: the file is written, so the pages read it whatever becomes of the request.
        await documents.ReloadAsync(command.BodyPath, CancellationToken.None);
        return CommandResult.Success(new RestoreRequirementFilesCommand([new RequirementFileState(command.BodyPath, before, after)], command));
    }
}
