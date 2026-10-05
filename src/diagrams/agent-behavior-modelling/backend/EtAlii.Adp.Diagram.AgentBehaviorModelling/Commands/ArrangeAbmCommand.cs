using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
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
public sealed class ArrangeAbmCommandHandler : ICommandHandler<ArrangeAbmCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ArrangeAbmCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return Task.FromResult(CommandResult.Failure(
                "This behavior model was opened without a registration, so it has no dragged positions to forget."));
        }

        string before;
        try
        {
            before = SharedDocumentReader.ReadAllText(command.AdpPath);
            if (RegistrationLayout.Read(command.AdpPath).Count == 0)
            {
                return Task.FromResult(CommandResult.Failure("This behavior model is already arranged."));
            }

            RegistrationLayout.Prune(command.AdpPath, new HashSet<string>(StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not arrange this behavior model: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(new RestoreAbmRegistrationCommand(command.AdpPath, before, command)));
    }
}

/// <inheritdoc cref="RestoreAbmRegistrationCommand" />
public sealed class RestoreAbmRegistrationCommandHandler : ICommandHandler<RestoreAbmRegistrationCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RestoreAbmRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            AdpFileWriter.Save(command.AdpPath, command.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not restore the registration: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(command.Redo));
    }
}
