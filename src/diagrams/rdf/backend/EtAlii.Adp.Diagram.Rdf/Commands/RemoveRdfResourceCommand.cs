using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Removes a resource and every triple it is subject or object of, as one undo (Requirement 5.3).</summary>
public sealed record RemoveRdfResourceCommand(string BodyPath, string Iri) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveRdfResourceCommand" />
public sealed class RemoveRdfResourceCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<RemoveRdfResourceCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, RemoveRdfResourceCommand command)
    {
        return Outcome(
            RdfWriter.RemoveResource(body, model, command.Iri));
    }
}
