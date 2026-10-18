using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Arranges a behavior model: forgets every position a node was dragged to, so the whole tree is
/// drawn by <see cref="AbmLayout"/> again.
/// </summary>
/// <remarks>
/// <b>The tidy tree is the arrangement.</b> A behavior tree's layout is computed from the tree -
/// the order is the meaning - and a drag is only an override kept in the registration. So the
/// least cluttered drawing is the computed one, and arranging means dropping the overrides. The
/// Markdown is never touched; one undo puts the registration back byte for byte.
/// </remarks>
/// <param name="AdpPath">The registration whose <c>layout:</c> block holds the dragged positions.</param>
public sealed record ArrangeAbmCommand(string AdpPath) : ICommand;

/// <summary>Puts a registration back byte for byte - the inverse of an arrangement.</summary>
/// <param name="AdpPath">The registration to restore.</param>
/// <param name="Text">Its complete text as captured before the arrangement.</param>
/// <param name="Redo">The arrangement, so redoing the undo arranges again.</param>
public sealed record RestoreAbmRegistrationCommand(string AdpPath, string Text, ICommand Redo) : ICommand;

/// <inheritdoc cref="ArrangeAbmCommand" />
public sealed class ArrangeAbmCommandHandler(IFileWriter writer) : ICommandHandler<ArrangeAbmCommand>
{
    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(ArrangeAbmCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return CommandResult.Failure(
                "This behavior model was opened without a registration, so it has no dragged positions to forget.");
        }

        string before;
        try
        {
            before = await SharedDocumentReader.ReadAllTextAsync(command.AdpPath, cancellationToken);
            if (RegistrationLayout.Read(command.AdpPath).Count == 0)
            {
                return CommandResult.Failure("This behavior model is already arranged.");
            }

            await RegistrationLayout.PruneAsync(writer, command.AdpPath, new HashSet<string>(StringComparer.Ordinal), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not arrange this behavior model: {exception.Message}");
        }

        return CommandResult.Success(new RestoreAbmRegistrationCommand(command.AdpPath, before, command));
    }
}

/// <inheritdoc cref="RestoreAbmRegistrationCommand" />
public sealed class RestoreAbmRegistrationCommandHandler(IFileWriter writer) : ICommandHandler<RestoreAbmRegistrationCommand>
{
    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(RestoreAbmRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await writer.SaveAsync(command.AdpPath, command.Text, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the registration: {exception.Message}");
        }

        return CommandResult.Success(command.Redo);
    }
}
