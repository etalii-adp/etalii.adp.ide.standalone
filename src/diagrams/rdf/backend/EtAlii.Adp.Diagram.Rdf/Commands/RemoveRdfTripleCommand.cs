using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Removes one triple, matched by its three values (Requirement 5.2).</summary>
public sealed record RemoveRdfTripleCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string ObjectIri, string ObjectLiteral = "", string ObjectLanguage = "", string ObjectDatatypeIri = "") : ICommand;

/// <inheritdoc cref="RemoveRdfTripleCommand" />
public sealed class RemoveRdfTripleCommandHandler(IRdfDocumentStore documents) : ICommandHandler<RemoveRdfTripleCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveRdfTripleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfEdits.TripleOf(
                entry.Model, command.SubjectIri, command.PredicateIri,
                command.ObjectIri, command.ObjectLiteral, command.ObjectLanguage, command.ObjectDatatypeIri) is { } triple
                ? RdfWriter.RemoveTriple(entry.Document, entry.Model, triple)
                : "That triple is not in the file as it stands, so there is nothing to remove.");
    }
}
