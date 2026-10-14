using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Rewrites one literal object in place - what a label or documentation edit is. The triple is
/// matched by subject, predicate and its current literal value; the new value replaces exactly
/// that token.
/// </summary>
public sealed record ReplaceRdfObjectLiteralCommand(
    string BodyPath, string SubjectIri, string PredicateIri,
    string OldLexical, string OldLanguage, string OldDatatypeIri,
    string NewLexical, string NewLanguage = "", string NewDatatypeIri = "") : IDocumentEditCommand;

/// <inheritdoc cref="ReplaceRdfObjectLiteralCommand" />
public sealed class ReplaceRdfObjectLiteralCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<ReplaceRdfObjectLiteralCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, ReplaceRdfObjectLiteralCommand command)
    {
        return Outcome(
            TripleOf(
                model, command.SubjectIri, command.PredicateIri,
                "", command.OldLexical, command.OldLanguage, command.OldDatatypeIri) is { } triple
                ? RdfWriter.ReplaceObjectLiteral(
                    body, model, triple,
                    command.NewLexical,
                    command.NewLanguage.Length > 0 ? command.NewLanguage : null,
                    command.NewDatatypeIri.Length > 0 ? command.NewDatatypeIri : null)
                : "That value is not in the file as it stands, so there is nothing to rewrite.");
    }
}
