using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Removes a shape and every blank-node subtree reachable only from it (Requirement 5.6). The
/// count the confirmation states comes from <see cref="ShaclWriter.CountShapeRemoval"/>, read
/// before the command is dispatched.
/// </summary>
public sealed record RemoveShaclShapeCommand(string BodyPath, string ShapeIri) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveShaclShapeCommand" />
public sealed class RemoveShaclShapeCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<RemoveShaclShapeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, RemoveShaclShapeCommand command)
    {
        return Outcome(
            ShaclWriter.RemoveShapeWithSubtrees(body, model, command.ShapeIri));
    }
}
