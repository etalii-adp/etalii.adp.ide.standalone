

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>States a new node shape (Requirement 5.2).</summary>
public sealed record CreateShaclNodeShapeCommand(string BodyPath, string ShapeIri) : ICommand;

/// <inheritdoc cref="CreateShaclNodeShapeCommand" />
public sealed class CreateShaclNodeShapeCommandHandler(IRdfDocumentStore documents) : ICommandHandler<CreateShaclNodeShapeCommand>
{
    public Task<CommandResult> ExecuteAsync(CreateShaclNodeShapeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            ShaclWriter.CreateNodeShape(entry.Document, entry.Model, command.ShapeIri));
    }
}
