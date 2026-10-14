using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>States a new node shape (Requirement 5.2).</summary>
public sealed record CreateShaclNodeShapeCommand(string BodyPath, string ShapeIri) : IDocumentEditCommand;

/// <inheritdoc cref="CreateShaclNodeShapeCommand" />
public sealed class CreateShaclNodeShapeCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<CreateShaclNodeShapeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, CreateShaclNodeShapeCommand command)
    {
        return Outcome(
            ShaclWriter.CreateNodeShape(body, model, command.ShapeIri));
    }
}
