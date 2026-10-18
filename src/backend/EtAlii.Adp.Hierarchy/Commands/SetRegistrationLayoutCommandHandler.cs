using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Stores one element's authored position in its registration's <c>layout:</c> block - the
/// command a canvas reposition dispatches, so moving a box is one undo away like every other
/// edit while the body file never changes by a byte (databricks-diagrams Requirement 7.6).
/// </summary>
/// <param name="AdpPath">Absolute path of the registration file; resolved by the caller.</param>
/// <param name="ElementId">The element whose position is being authored.</param>
/// <param name="X">The authored x, in the diagram's own canvas units.</param>
/// <param name="Y">The authored y, in the diagram's own canvas units.</param>
public sealed record SetRegistrationLayoutCommand(string AdpPath, string ElementId, double X, double Y) : ICommand;

/// <summary>
/// Removes one element's entry from the <c>layout:</c> block - the inverse of a
/// <see cref="SetRegistrationLayoutCommand"/> that created the entry, so undoing the first
/// move of an element returns the registration to having no entry at all.
/// </summary>
public sealed record RemoveRegistrationLayoutCommand(string AdpPath, string ElementId) : ICommand;

public sealed class SetRegistrationLayoutCommandHandler(IFileWriter writer) : ICommandHandler<SetRegistrationLayoutCommand>
{
    public async Task<CommandResult> ExecuteAsync(SetRegistrationLayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return CommandResult.Failure("The registration file is no longer there.");
        }

        RegistrationPosition? prior;
        try
        {
            prior = await RegistrationLayout.SetPositionAsync(
                writer, command.AdpPath, command.ElementId, new RegistrationPosition(command.X, command.Y), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not store the position: {exception.Message}");
        }

        // The inverse restores what was there before: the prior entry, or its absence.
        ICommand inverse = prior is { } previous
            ? command with { X = previous.X, Y = previous.Y }
            : new RemoveRegistrationLayoutCommand(command.AdpPath, command.ElementId);
        return CommandResult.Success(inverse);
    }
}

public sealed class RemoveRegistrationLayoutCommandHandler(IFileWriter writer) : ICommandHandler<RemoveRegistrationLayoutCommand>
{
    public async Task<CommandResult> ExecuteAsync(RemoveRegistrationLayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return CommandResult.Failure("The registration file is no longer there.");
        }

        var positions = RegistrationLayout.Read(command.AdpPath);
        if (!positions.TryGetValue(command.ElementId, out var existing))
        {
            return CommandResult.Failure("That element has no stored position any more.");
        }

        try
        {
            await RegistrationLayout.RemovePositionAsync(writer, command.AdpPath, command.ElementId, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not remove the position: {exception.Message}");
        }

        return CommandResult.Success(
            new SetRegistrationLayoutCommand(command.AdpPath, command.ElementId, existing.X, existing.Y));
    }
}
