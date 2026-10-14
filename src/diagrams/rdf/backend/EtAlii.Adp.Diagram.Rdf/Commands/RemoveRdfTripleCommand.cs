using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Removes one triple, matched by its three values (Requirement 5.2).</summary>
public sealed record RemoveRdfTripleCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string ObjectIri, string ObjectLiteral = "", string ObjectLanguage = "", string ObjectDatatypeIri = "") : IDocumentEditCommand;

/// <inheritdoc cref="RemoveRdfTripleCommand" />
public sealed class RemoveRdfTripleCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<RemoveRdfTripleCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, RemoveRdfTripleCommand command)
    {
        return Outcome(
            TripleOf(
                model, command.SubjectIri, command.PredicateIri,
                command.ObjectIri, command.ObjectLiteral, command.ObjectLanguage, command.ObjectDatatypeIri) is { } triple
                ? RdfWriter.RemoveTriple(body, model, triple)
                : "That triple is not in the file as it stands, so there is nothing to remove.");
    }
}
