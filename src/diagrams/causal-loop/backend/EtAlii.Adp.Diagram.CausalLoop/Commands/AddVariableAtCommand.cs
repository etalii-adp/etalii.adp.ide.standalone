using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
namespace EtAlii.Adp.Diagram.CausalLoop;

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
public sealed class AddVariableAtCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<AddVariableAtCommand>
{
    public Task<CommandResult> ExecuteAsync(AddVariableAtCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This causal loop diagram could not be read, so nothing can be added until it is fixed."));
        }

        var beforeBody = entry.Document.Text;

        var refusal = CausalLoopWriter.AddVariable(entry.Document, entry.Model, command.Id, command.Label);
        if (refusal.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var saveError = documents.Save(command.BodyPath);
        if (saveError.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(saveError));
        }

        RegistrationPosition? priorPosition;
        try
        {
            priorPosition = RegistrationLayout.SetPosition(
                command.RegistrationPath, $"variable:{command.Id}", new RegistrationPosition(command.X, command.Y));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The variable is stated; only its position could not be written. Rather than leave a
            // half-done edit that undo cannot fully reverse, put the document back and report.
            RestoreCausalLoopDocument.Restore(documents, command.BodyPath, beforeBody);
            return Task.FromResult(CommandResult.Failure($"Could not store the variable's position: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(
            new RestoreVariableAdditionCommand(
                command.BodyPath, beforeBody, command.RegistrationPath, $"variable:{command.Id}", priorPosition, command)));
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
public sealed class RestoreVariableAdditionCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<RestoreVariableAdditionCommand>
{
    public Task<CommandResult> ExecuteAsync(
        RestoreVariableAdditionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            RestoreCausalLoopDocument.Restore(documents, command.BodyPath, command.BodyText);
            if (command.PriorPosition is { } position)
            {
                RegistrationLayout.SetPosition(command.RegistrationPath, command.ElementId, position);
            }
            else
            {
                RegistrationLayout.RemovePosition(command.RegistrationPath, command.ElementId);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not undo the variable: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}

/// <summary>Restoring a document's exact bytes, shared by the commands that need to undo an edit to it.</summary>
internal static class RestoreCausalLoopDocument
{
    public static void Restore(ICausalLoopDocumentStore documents, string bodyPath, string text)
    {
        AdpFileWriter.Save(bodyPath, text);
        documents.Reload(bodyPath);
    }
}
