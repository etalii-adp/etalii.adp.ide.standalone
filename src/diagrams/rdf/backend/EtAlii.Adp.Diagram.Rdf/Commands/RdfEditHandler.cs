using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// A handler of a command that edits an RDF file: the shared run, which edits a copy, saves it and
/// carries a warning of the save to the result (code-structure FR-033), with the model the
/// family's parser reads. Every reading's commands - RDF, OWL, SKOS and SHACL - derive from it.
/// </summary>
/// <remarks>
/// The inverse is the run's whole-document restore, deliberately: this family's operations touch
/// several distant ranges at once (rename-with-references, remove-with-edges), the documents are
/// small, and a snapshot cannot be wrong. The redo of that inverse is the original command, so a
/// redo re-runs the same edit against the restored document.
/// </remarks>
/// <typeparam name="TCommand">The command handled.</typeparam>
public abstract class RdfEditHandler<TCommand>(IRdfDocumentStore documents)
    : DocumentEditHandler<TCommand, IRdfDocumentStore, LineDocument, RdfModel>(documents)
    where TCommand : IDocumentEditCommand
{
    /// <inheritdoc />
    /// <remarks>The run edits only a body the store could read, so its copy parses.</remarks>
    protected sealed override RdfModel Parse(LineDocument body) => RdfParser.Parse(body);

    /// <summary>A writer's answer as an outcome: its refusal, or applied when it has none.</summary>
    protected static EditOutcome Outcome(string refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return refusal.Length > 0 ? EditOutcome.Refused(refusal) : EditOutcome.Applied;
    }

    /// <summary>
    /// The triple a command addresses, matched by its three values: an IRI object where
    /// <paramref name="objectIri"/> is set, and a literal matched on lexical form, language and
    /// datatype otherwise - empty strings meaning absent, as commands carry them.
    /// </summary>
    protected static RdfTriple? TripleOf(
        RdfModel model, string subjectIri, string predicateIri,
        string objectIri, string lexical, string language, string datatypeIri)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Triples.FirstOrDefault(t =>
            t.Subject is IriTerm subject
            && subject.Iri == subjectIri
            && t.Predicate.Iri == predicateIri
            && (objectIri.Length > 0
                ? t.Object is IriTerm o && o.Iri == objectIri
                : t.Object is LiteralTerm l
                    && l.Lexical == lexical
                    && (l.Language ?? "") == language
                    && (l.DatatypeIri ?? "") == datatypeIri));
    }
}
