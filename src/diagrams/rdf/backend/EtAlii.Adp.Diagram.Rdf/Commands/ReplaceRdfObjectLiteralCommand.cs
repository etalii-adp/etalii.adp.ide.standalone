using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Rewrites one literal object in place - what a label or documentation edit is. The triple is
/// matched by subject, predicate and its current literal value; the new value replaces exactly
/// that token.
/// </summary>
public sealed record ReplaceRdfObjectLiteralCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string OldLexical, string OldLanguage, string OldDatatypeIri,
    string NewLexical, string NewLanguage = "", string NewDatatypeIri = "") : ICommand;

/// <inheritdoc cref="ReplaceRdfObjectLiteralCommand" />
public sealed class ReplaceRdfObjectLiteralCommandHandler(IRdfDocumentStore documents) : ICommandHandler<ReplaceRdfObjectLiteralCommand>
{
    public Task<CommandResult> ExecuteAsync(ReplaceRdfObjectLiteralCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
            RdfEdits.TripleOf(
                entry.Model, command.SubjectIri, command.PredicateIri,
                "", command.OldLexical, command.OldLanguage, command.OldDatatypeIri) is { } triple
                ? RdfWriter.ReplaceObjectLiteral(
                    entry.Document, entry.Model, triple,
                    command.NewLexical,
                    command.NewLanguage.Length > 0 ? command.NewLanguage : null,
                    command.NewDatatypeIri.Length > 0 ? command.NewDatatypeIri : null)
                : "That value is not in the file as it stands, so there is nothing to rewrite.");
    }
}
