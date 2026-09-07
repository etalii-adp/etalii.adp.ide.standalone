using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Removes a shape and every blank-node subtree reachable only from it (Requirement 5.6). The
/// count the confirmation states comes from <see cref="ShaclWriter.CountShapeRemoval"/>, read
/// before the command is dispatched.
/// </summary>
public sealed record RemoveShaclShapeCommand(string BodyPath, string ShapeIri) : ICommand;

/// <inheritdoc cref="RemoveShaclShapeCommand" />
public sealed class RemoveShaclShapeCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveShaclShapeCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveShaclShapeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.RemoveShapeWithSubtrees(entry.Document, entry.Model, command.ShapeIri));
    }
}
