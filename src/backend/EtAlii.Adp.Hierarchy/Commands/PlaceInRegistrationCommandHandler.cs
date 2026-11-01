using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Stores one element's position in its registration's <c>layout:</c> block through FBL's
/// registration writer (FBL §8.3): the entry spliced in at the byte order of its id's UTF-8 encoding,
/// its numbers in <c>{decimals: 3}</c> form, a new line taking the registration's own line ending,
/// and every other byte left as it was. The way a tool whose binding asks for the library's writer
/// writes a position, beside <see cref="SetRegistrationLayoutCommand"/>, which rewrites the block
/// the way core always has.
/// </summary>
/// <param name="AdpPath">Absolute path of the registration file; resolved by the caller.</param>
/// <param name="ElementId">The element whose position is being authored.</param>
/// <param name="X">The authored x, in the diagram's own canvas units.</param>
/// <param name="Y">The authored y, in the diagram's own canvas units.</param>
public sealed record PlaceInRegistrationCommand(string AdpPath, string ElementId, double X, double Y) : ICommand;

/// <summary>
/// Puts a registration's bytes back as they were before a <see cref="PlaceInRegistrationCommand"/>:
/// its inverse, so undoing a move restores the file byte for byte.
/// </summary>
/// <param name="AdpPath">The registration to restore.</param>
/// <param name="Before">Its bytes before the placement.</param>
/// <param name="After">
/// Its bytes as the placement left them. A file that no longer holds them was changed since, and the
/// restore refuses with FBL §7.2's sentence rather than write over that change.
/// </param>
/// <param name="Redo">The placement itself, which a redo runs again.</param>
public sealed record RestoreRegistrationCommand(string AdpPath, ReadOnlyMemory<byte> Before, ReadOnlyMemory<byte> After, PlaceInRegistrationCommand Redo) : ICommand;

public sealed class PlaceInRegistrationCommandHandler(IFileWriter writer) : ICommandHandler<PlaceInRegistrationCommand>
{
    public async Task<CommandResult> ExecuteAsync(PlaceInRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return CommandResult.Failure("The registration file is no longer there.");
        }

        byte[] before;
        try
        {
            before = SharedDocumentReader.ReadAllBytes(command.AdpPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not store the position: {exception.Message}");
        }

        // KnownIds is left unset: the command knows no reading, so no entry is taken for stale and
        // pruned by a move.
        var registration = OpenRegistration.Open(before);
        if (registration.Change(new ModelChange.Place(command.ElementId, command.X, command.Y)) is PlanResult.Refused refused)
        {
            return CommandResult.Failure(refused.Reason);
        }

        var after = registration.Bytes;
        if (after.AsSpan().SequenceEqual(before))
        {
            // Placed where it already was: nothing is written, so there is nothing to undo.
            return CommandResult.Success();
        }

        try
        {
            await writer.SaveAsync(command.AdpPath, after, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not store the position: {exception.Message}");
        }

        return CommandResult.Success(new RestoreRegistrationCommand(command.AdpPath, before, after, command));
    }
}

/// <summary>
/// Carries out <see cref="RestoreRegistrationCommand"/>. The drift check is the one
/// <see cref="SplicedFile"/> makes before an undo (FBL §7.2), made here against the file on disk
/// because the registration's own history ends with the command that placed it.
/// </summary>
public sealed class RestoreRegistrationCommandHandler(IFileWriter writer) : ICommandHandler<RestoreRegistrationCommand>
{
    public async Task<CommandResult> ExecuteAsync(RestoreRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.AdpPath))
        {
            return CommandResult.Failure("The registration file is no longer there.");
        }

        byte[] current;
        try
        {
            current = SharedDocumentReader.ReadAllBytes(command.AdpPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the position: {exception.Message}");
        }

        if (!command.After.Span.SequenceEqual(current))
        {
            // Changed since the move, by hand or by another program: refused, and nothing written.
            return CommandResult.Failure(SplicedFile.DriftUndo);
        }

        try
        {
            await writer.SaveAsync(command.AdpPath, command.Before.ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the position: {exception.Message}");
        }

        return CommandResult.Success(command.Redo);
    }
}
