using System.Collections;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Carries a renamed task's stored position from its old id to its new one in the registration's <c>layout:</c> block
/// (finding B16, Requirement 6.6), through the FBL library's registration writer (Requirement 5.5): one edit that places
/// the new id and takes the old id's entry out, every other byte left as it was.
/// </summary>
/// <param name="AdpPath">Absolute path of the registration file; resolved by the caller.</param>
/// <param name="FromId">The id the position is stored under now.</param>
/// <param name="ToId">The id it is stored under after the rename.</param>
/// <param name="X">The stored x.</param>
/// <param name="Y">The stored y.</param>
public sealed record CarryDatabricksPositionCommand(string AdpPath, string FromId, string ToId, double X, double Y) : ICommand;

/// <summary>
/// Puts a registration's bytes back as they were before a <see cref="CarryDatabricksPositionCommand"/>: its inverse, so
/// undoing a rename restores the registration byte for byte.
/// </summary>
/// <param name="AdpPath">The registration to restore.</param>
/// <param name="Before">Its bytes before the carry.</param>
/// <param name="After">
/// Its bytes as the carry left them. A file that no longer holds them was changed since, and the restore refuses with
/// FBL §7.2's sentence rather than write over that change.
/// </param>
/// <param name="Redo">The carry itself, which a redo runs again.</param>
public sealed record RestoreDatabricksRegistrationCommand(string AdpPath, ReadOnlyMemory<byte> Before, ReadOnlyMemory<byte> After, CarryDatabricksPositionCommand Redo) : ICommand;

/// <inheritdoc cref="CarryDatabricksPositionCommand" />
/// <remarks>
/// The registration writer removes a layout entry only as stale, for an id outside its <see cref="OpenRegistration.KnownIds"/>
/// (FBL §8.5). The known ids are therefore every id but <see cref="CarryDatabricksPositionCommand.FromId"/>, so the one
/// entry taken out is the renamed task's: the known ids serve this one removal and nothing else, and no stale entry is
/// pruned, because FBL 8.5's pruning is not adopted (ruled by the user in chat, 2026-10-11).
/// </remarks>
public sealed class CarryDatabricksPositionCommandHandler(IFileWriter writer) : ICommandHandler<CarryDatabricksPositionCommand>
{
    private readonly IFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(CarryDatabricksPositionCommand command, CancellationToken cancellationToken = default)
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

        var registration = OpenRegistration.Open(before);
        registration.KnownIds = new EveryIdBut(command.FromId);
        if (registration.Change(new ModelChange.Place(command.ToId, command.X, command.Y)) is PlanResult.Refused refused)
        {
            return CommandResult.Failure(refused.Reason);
        }

        var after = registration.Bytes;
        if (after.AsSpan().SequenceEqual(before))
        {
            return CommandResult.Success();
        }

        try
        {
            await _writer.SaveAsync(command.AdpPath, after, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not store the position: {exception.Message}");
        }

        return CommandResult.Success(new RestoreDatabricksRegistrationCommand(command.AdpPath, before, after, command));
    }

    /// <summary>
    /// The ids the registration writer takes for known: all but one. The writer asks a known set only whether it holds
    /// an id, so the members a set cannot answer without listing its ids refuse.
    /// </summary>
    private sealed class EveryIdBut(string excluded) : IReadOnlySet<string>
    {
        public int Count => throw Unlisted();

        public bool Contains(string item) => !string.Equals(item, excluded, StringComparison.Ordinal);

        public IEnumerator<string> GetEnumerator() => throw Unlisted();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsProperSubsetOf(IEnumerable<string> other) => throw Unlisted();

        public bool IsProperSupersetOf(IEnumerable<string> other) => throw Unlisted();

        public bool IsSubsetOf(IEnumerable<string> other) => throw Unlisted();

        public bool IsSupersetOf(IEnumerable<string> other) => throw Unlisted();

        public bool Overlaps(IEnumerable<string> other) => throw Unlisted();

        public bool SetEquals(IEnumerable<string> other) => throw Unlisted();

        private static NotSupportedException Unlisted() => new("Every id but one cannot be listed.");
    }
}

/// <inheritdoc cref="RestoreDatabricksRegistrationCommand" />
/// <remarks>
/// The drift check is the one <see cref="SplicedFile"/> makes before an undo (FBL §7.2), made against the file on disk.
/// </remarks>
public sealed class RestoreDatabricksRegistrationCommandHandler(IFileWriter writer) : ICommandHandler<RestoreDatabricksRegistrationCommand>
{
    private readonly IFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(RestoreDatabricksRegistrationCommand command, CancellationToken cancellationToken = default)
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
            return CommandResult.Failure(SplicedFile.DriftUndo);
        }

        try
        {
            await _writer.SaveAsync(command.AdpPath, command.Before.ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure($"Could not restore the position: {exception.Message}");
        }

        return CommandResult.Success(command.Redo);
    }
}
