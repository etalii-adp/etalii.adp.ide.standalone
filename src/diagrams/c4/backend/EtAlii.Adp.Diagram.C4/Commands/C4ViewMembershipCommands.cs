using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Records where the user dragged an element on one view. A position is view state, not model
/// state: it goes in ADP's sidecar and never into the <c>.dsl</c>, which another ecosystem owns
/// (c4-diagrams Requirements 3.5, 8.3).
/// </summary>
public sealed record MoveC4ElementCommand(
    string BodyPath,
    string ViewKey,
    string ElementId,
    double X,
    double Y) : ICommand;

/// <summary>Puts an element back where it was, including back to "wherever the layout computes".</summary>
public sealed record RestoreC4ElementPositionCommand(
    string BodyPath,
    string ViewKey,
    string ElementId,
    double? X,
    double? Y) : ICommand;

internal sealed class MoveC4ElementCommandHandler(IC4DocumentStore documents, C4LayoutSidecar sidecar)
    : ICommandHandler<MoveC4ElementCommand>
{
    public Task<CommandResult> ExecuteAsync(MoveC4ElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = documents.WorkspaceOf(command.BodyPath);
        if (workspace.Find(command.ElementId) is null)
        {
            return Task.FromResult(CommandResult.Failure($"'{command.ElementId}' is not in this model any more."));
        }

        // The inverse restores the previous authored position, or clears it when there was
        // none - so undoing the first drag of an element hands it back to the layout rather
        // than pinning it where it happened to have been computed.
        var previous = sidecar.Read(command.BodyPath, command.ViewKey).TryGetValue(command.ElementId, out var was)
            ? new RestoreC4ElementPositionCommand(command.BodyPath, command.ViewKey, command.ElementId, was.X, was.Y)
            : new RestoreC4ElementPositionCommand(command.BodyPath, command.ViewKey, command.ElementId, null, null);

        // Succeeds either way: the element HAS moved, and refusing the drag because its new
        // position could not be recorded would lose work the user can see. The warning is
        // what stops the position quietly not being there on the next open.
        var warning = sidecar.Write(command.BodyPath, command.ViewKey, command.ElementId, new C4SidecarPosition(command.X, command.Y));
        documents.Touch(command.BodyPath);
        return Task.FromResult(CommandResult.Success(previous, warning));
    }
}

internal sealed class RestoreC4ElementPositionCommandHandler(IC4DocumentStore documents, C4LayoutSidecar sidecar)
    : ICommandHandler<RestoreC4ElementPositionCommand>
{
    public Task<CommandResult> ExecuteAsync(RestoreC4ElementPositionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var current = sidecar.Read(command.BodyPath, command.ViewKey).TryGetValue(command.ElementId, out var was)
            ? new MoveC4ElementCommand(command.BodyPath, command.ViewKey, command.ElementId, was.X, was.Y)
            : null;

        var warning = command.X is { } x && command.Y is { } y
            ? sidecar.Write(command.BodyPath, command.ViewKey, command.ElementId, new C4SidecarPosition(x, y))
            : sidecar.Remove(command.BodyPath, command.ViewKey, command.ElementId);

        documents.Touch(command.BodyPath);

        // Redoing a "restore to computed" means moving back to where the drag put it; when
        // there was nothing to move back to, the inverse is a no-op restore.
        return Task.FromResult(CommandResult.Success(
            (ICommand?)current ?? new RestoreC4ElementPositionCommand(command.BodyPath, command.ViewKey, command.ElementId, null, null),
            warning));
    }
}
