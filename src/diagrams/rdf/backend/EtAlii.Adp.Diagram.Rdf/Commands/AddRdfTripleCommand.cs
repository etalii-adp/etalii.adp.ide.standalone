using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// States one more triple (Requirement 5.1). The object is an IRI where
/// <paramref name="ObjectIri"/> is set, and a literal built from the remaining fields otherwise -
/// empty strings meaning absent.
/// </summary>
/// <param name="BodyPath">The RDF file.</param>
/// <param name="SubjectIri">The subject's full IRI.</param>
/// <param name="PredicateIri">The predicate's full IRI.</param>
/// <param name="ObjectIri">The object's full IRI, or empty for a literal object.</param>
/// <param name="ObjectLiteral">The literal object's lexical form.</param>
/// <param name="ObjectLanguage">The literal's language tag, without its <c>@</c>.</param>
/// <param name="ObjectDatatypeIri">The literal's datatype IRI.</param>
public sealed record AddRdfTripleCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string ObjectIri, string ObjectLiteral = "", string ObjectLanguage = "", string ObjectDatatypeIri = "") : ICommand;

/// <inheritdoc cref="AddRdfTripleCommand" />
public sealed class AddRdfTripleCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddRdfTripleCommand>
{
    public Task<CommandResult> ExecuteAsync(AddRdfTripleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        RdfTerm objectTerm = command.ObjectIri.Length > 0
            ? new IriTerm(command.ObjectIri, "")
            : new LiteralTerm(
                command.ObjectLiteral,
                command.ObjectDatatypeIri.Length > 0 ? command.ObjectDatatypeIri : null,
                command.ObjectLanguage.Length > 0 ? command.ObjectLanguage : null,
                "");

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfWriter.AddTriple(entry.Document, entry.Model, command.SubjectIri, command.PredicateIri, objectTerm));
    }
}
