using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Declares a variable and, in the same undoable step, authors its position where the user
/// dropped or right-clicked - so a dropped variable lands under the pointer rather than at
/// whatever ring slot the default layout would give it, and undoing the drop removes both.
/// </summary>
/// <remarks>
/// Two files change: the <c>.cld</c> gains the variable statement, and the registration's
/// <c>.adp</c> gains the position. They move together because they are one gesture; the inverse
/// restores both. Where there is no registration to write a position into, the caller uses the
/// plain <see cref="AddVariableCommand"/> instead and the ring layout places the variable.
/// </remarks>
public sealed record AddVariableAtCommand(
    string BodyPath, string RegistrationPath, string Id, string Label, double X, double Y) : ICommand;

/// <inheritdoc cref="AddVariableAtCommand" />
public sealed class AddVariableAtCommandHandler(ICausalLoopDocumentStore documents, IFileWriter writer)
    : ICommandHandler<AddVariableAtCommand>
{
    public async Task<CommandResult> ExecuteAsync(AddVariableAtCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await documents.GetOrLoadAsync(command.BodyPath, cancellationToken);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                "This causal loop diagram could not be read, so nothing can be added until it is fixed.");
        }

        var beforeBody = entry.Document.Text;
        if (CausalLoopEdits.AdditionRefusal(entry.Model, command.Id, command.Label) is { } refusal)
        {
            return CommandResult.Failure(refusal);
        }

        // Edited on a copy, as every other edit is, so a refusal leaves nothing behind for the next save.
        var body = CausalLoopBody.Parse(beforeBody);
        var added = CausalLoopEdits.AddVariable(body, command.Id, command.Label);
        if (!added.WasApplied)
        {
            return CommandResult.Failure(added.Refusal!);
        }

        var saved = await documents.SaveAsync(command.BodyPath, body, cancellationToken);
        if (saved.Failed)
        {
            return CommandResult.Failure(saved.Error);
        }

        RegistrationPosition? priorPosition;
        try
        {
            priorPosition = await RegistrationLayout.SetPositionAsync(
                writer, command.RegistrationPath, $"variable:{command.Id}", new RegistrationPosition(command.X, command.Y), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The variable is stated; only its position could not be written. Rather than leave a
            // half-done edit that undo cannot fully reverse, put the document back and report.
            // Not cancelled: a put-back stopped halfway would leave the half-done edit it exists to undo.
            await RestoreCausalLoopDocument.RestoreAsync(documents, writer, command.BodyPath, beforeBody, CancellationToken.None);
            return CommandResult.Failure($"Could not store the variable's position: {exception.Message}");
        }

        // Built from the save, so a warning of it reaches the user (code-structure FR-022).
        return CommandResult.FromSave(
            saved,
            new RestoreVariableAdditionCommand(
                command.BodyPath, beforeBody, command.RegistrationPath, $"variable:{command.Id}", priorPosition, command));
    }
}

/// <summary>
/// The inverse of an <see cref="AddVariableAtCommand"/>: it puts the document back byte for byte
/// and restores the registration position to what it was - the prior value, or its absence.
/// </summary>
/// <param name="BodyPath">The document to restore.</param>
/// <param name="BodyText">Its complete text as captured before the variable was added.</param>
/// <param name="RegistrationPath">The registration whose layout entry is restored.</param>
/// <param name="ElementId">The layout key the position was stored under.</param>
/// <param name="PriorPosition">The position that was there before, or null when there was none.</param>
/// <param name="Redo">The original command, so redoing the undo adds the variable again.</param>
public sealed record RestoreVariableAdditionCommand(
    string BodyPath,
    string BodyText,
    string RegistrationPath,
    string ElementId,
    RegistrationPosition? PriorPosition,
    ICommand Redo) : ICommand;

/// <inheritdoc cref="RestoreVariableAdditionCommand" />
public sealed class RestoreVariableAdditionCommandHandler(ICausalLoopDocumentStore documents, IFileWriter writer)
    : ICommandHandler<RestoreVariableAdditionCommand>
{
    public async Task<CommandResult> ExecuteAsync(
        RestoreVariableAdditionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await RestoreCausalLoopDocument.RestoreAsync(documents, writer, command.BodyPath, command.BodyText, cancellationToken);
            if (command.PriorPosition is { } position)
            {
                await RegistrationLayout.SetPositionAsync(writer, command.RegistrationPath, command.ElementId, position, cancellationToken);
            }
            else
            {
                await RegistrationLayout.RemovePositionAsync(writer, command.RegistrationPath, command.ElementId, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not undo the variable: {exception.Message}");
        }

        return CommandResult.Success(command.Redo);
    }
}

/// <summary>Restoring a document's exact bytes, shared by the commands that need to undo an edit to it.</summary>
internal static class RestoreCausalLoopDocument
{
    public static async Task RestoreAsync(ICausalLoopDocumentStore documents, IFileWriter writer, string bodyPath, string text, CancellationToken cancellationToken)
    {
        await writer.SaveAsync(bodyPath, text, cancellationToken);
        // Not with the request's token: the file is written, so the store reads it whatever
        // becomes of the request.
        await documents.ReloadAsync(bodyPath, CancellationToken.None);
    }
}
