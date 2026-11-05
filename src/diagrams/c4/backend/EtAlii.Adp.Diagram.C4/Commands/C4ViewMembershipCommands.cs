using EtAlii.Adp.Files;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Records where the user dragged an element on one view. A position is view state, not model
/// state: it goes where <see cref="C4Positions"/> keeps it and never into the <c>.dsl</c>, which
/// another ecosystem owns (c4-diagrams Requirements 3.5, 8.3; c4-disl-fbl Requirement 7).
/// </summary>
/// <param name="RegistrationPath">The registration the view was opened through, whose <c>layout:</c> block may keep the position; null for none.</param>
public sealed record MoveC4ElementCommand(
    string BodyPath,
    string ViewKey,
    string ElementId,
    double X,
    double Y,
    string? RegistrationPath = null) : ICommand;

/// <summary>
/// Puts the file a position was written to back as it was before, or as it was after on a redo: the undo of a move is a
/// restore of that file's bytes, refused when the file has changed since (c4-disl-fbl task 16).
/// </summary>
/// <param name="BodyPath">The body whose positions the file keeps, whose sessions redraw.</param>
/// <param name="Path">The file the position was written to: the layout file or the registration.</param>
/// <param name="Bytes">The bytes to put back.</param>
/// <param name="Expected">The bytes the file must hold now, or the restore is refused.</param>
public sealed record RestoreC4LayoutCommand(string BodyPath, string Path, byte[] Bytes, byte[] Expected) : ICommand;

/// <remarks>
/// Not on <see cref="C4EditHandler{TCommand}"/>: a position is written beside the body and the body is
/// never edited, so the edit run - a copy of the body, saved - has nothing to do here.
/// </remarks>
internal sealed class MoveC4ElementCommandHandler(IC4DocumentStore documents, C4Positions positions)
    : ICommandHandler<MoveC4ElementCommand>
{
    /// <summary>What a drag of an element written without an identifier answers (Q7).</summary>
    public const string Unnamed = "This element is written without an identifier, so its position would be lost at the next edit above it; give it an identifier in the file to arrange it by hand.";

    public async Task<CommandResult> ExecuteAsync(MoveC4ElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = (await documents.GetOrLoadAsync(command.BodyPath, cancellationToken)).Model;
        if (workspace.Find(command.ElementId) is null)
        {
            return CommandResult.Failure($"'{command.ElementId}' is not in this model any more.");
        }

        var stored = C4Body.Of(workspace).StoredIds();
        if (!stored.Contains(command.ElementId))
        {
            return CommandResult.Failure(Unnamed);
        }

        // Succeeds either way: the element HAS moved, and refusing the drag because its new
        // position could not be recorded would lose work the user can see. The warning is
        // what stops the position quietly not being there on the next open.
        (RestoreC4LayoutCommand? inverse, string warning) = await positions.StoreAsync(command.BodyPath, command.RegistrationPath, command.ViewKey, command.ElementId, command.X, command.Y, stored, cancellationToken);
        await documents.TouchAsync(command.BodyPath, cancellationToken);
        return inverse is null
            ? CommandResult.Success(new RestoreC4LayoutCommand(command.BodyPath, "", [], []), warning)
            : CommandResult.Success(inverse, warning);
    }
}

internal sealed class RestoreC4LayoutCommandHandler(IC4DocumentStore documents, IFileWriter writer)
    : ICommandHandler<RestoreC4LayoutCommand>
{
    public async Task<CommandResult> ExecuteAsync(RestoreC4LayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // A move that wrote nothing is undone by writing nothing.
        if (command.Path.Length == 0)
        {
            return CommandResult.Success(command);
        }

        byte[] now;
        try
        {
            now = await File.ReadAllBytesAsync(command.Path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandResult.Failure(C4Positions.Unreadable(command.BodyPath));
        }

        if (!now.AsSpan().SequenceEqual(command.Expected))
        {
            return CommandResult.Failure("The layout has changed since this move, so it cannot be put back.");
        }

        await writer.SaveAsync(command.Path, command.Bytes, cancellationToken);
        await documents.TouchAsync(command.BodyPath, cancellationToken);
        return CommandResult.Success(command with { Bytes = command.Expected, Expected = command.Bytes });
    }
}
