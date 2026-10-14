using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Creates one concept as one command: <c>rdf:type skos:Concept</c>, a preferred label in the
/// display language, and - where a scheme is named - <c>skos:inScheme</c>, three splices under
/// one snapshot inverse (skos-diagram Requirement 5.3).
/// </summary>
/// <param name="BodyPath">The SKOS file.</param>
/// <param name="ConceptIri">The minted IRI, collision-refused before dispatch.</param>
/// <param name="PrefLabel">The label the dialog provided.</param>
/// <param name="Language">The label's language tag - the session's display language.</param>
/// <param name="SchemeIri">The scheme to file it into, or empty for the unfiled band.</param>
public sealed record AddSkosConceptCommand(
    string BodyPath, string ConceptIri, string PrefLabel, string Language, string SchemeIri) : IDocumentEditCommand;

/// <summary>
/// Removes every asserted triple of one hierarchy or related pair - a both-ways pair goes as one
/// command, stated in the confirmation (skos-diagram Requirement 5.6).
/// </summary>
/// <param name="BodyPath">The SKOS file.</param>
/// <param name="AIri">One end - the canonical id's first IRI (the narrower end for hierarchy).</param>
/// <param name="BIri">The other end.</param>
/// <param name="Hierarchy">True for a broader/narrower pair, false for related.</param>
public sealed record DisconnectSkosPairCommand(
    string BodyPath, string AIri, string BIri, bool Hierarchy) : IDocumentEditCommand;

/// <inheritdoc cref="AddSkosConceptCommand" />
public sealed class AddSkosConceptCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<AddSkosConceptCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, AddSkosConceptCommand command)
    {
        // Three splices, one snapshot inverse. The writer's coordinates are valid only for
        // the parse they came from, so the model is re-read between splices rather than
        // trusted across them (the family's own stale-span rule).
        var refusal = RdfWriter.AddTriple(
            body, model,
            command.ConceptIri, RdfVocabulary.Type, new IriTerm(SkosVocabulary.Concept, ""));
        if (refusal.Length > 0)
        {
            return Outcome(refusal);
        }

        refusal = RdfWriter.AddTriple(
            body, RdfParser.Parse(body),
            command.ConceptIri, SkosVocabulary.PrefLabel,
            new LiteralTerm(command.PrefLabel, null, command.Language.Length > 0 ? command.Language : null, ""));
        if (refusal.Length > 0 || command.SchemeIri.Length == 0)
        {
            return Outcome(refusal);
        }

        return Outcome(RdfWriter.AddTriple(
            body, RdfParser.Parse(body),
            command.ConceptIri, SkosVocabulary.InScheme, new IriTerm(command.SchemeIri, "")));
    }
}

/// <inheritdoc cref="DisconnectSkosPairCommand" />
public sealed class DisconnectSkosPairCommandHandler(IRdfDocumentStore documents) : RdfEditHandler<DisconnectSkosPairCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, RdfModel model, DisconnectSkosPairCommand command)
    {
        // One asserted direction or both: each splice invalidates the previous parse's
        // coordinates, so the pair is re-found on a fresh model until none remains.
        var removed = 0;
        while (true)
        {
            var current = removed == 0 ? model : RdfParser.Parse(body);
            var triple = (command.Hierarchy
                    ? SkosPair.Hierarchy(current, command.AIri, command.BIri)
                    : SkosPair.Related(current, command.AIri, command.BIri))
                .FirstOrDefault();
            if (triple is null)
            {
                return Outcome(removed > 0
                    ? ""
                    : "That connection is not in the file as it stands, so there is nothing to disconnect.");
            }

            var refusal = RdfWriter.RemoveTriple(body, current, triple);
            if (refusal.Length > 0)
            {
                return Outcome(refusal);
            }

            removed++;
        }
    }

    /// <summary>The pair lookups over a given model - the handler's fresh-parse loop needs them model-first.</summary>
    private static class SkosPair
    {
        public static IEnumerable<RdfTriple> Hierarchy(RdfModel model, string narrowerIri, string broaderIri) =>
            model.Triples.Where(t =>
                Is(t, narrowerIri, SkosVocabulary.Broader, broaderIri)
                || Is(t, broaderIri, SkosVocabulary.Narrower, narrowerIri));

        public static IEnumerable<RdfTriple> Related(RdfModel model, string aIri, string bIri) =>
            model.Triples.Where(t =>
                Is(t, aIri, SkosVocabulary.Related, bIri)
                || Is(t, bIri, SkosVocabulary.Related, aIri));

        private static bool Is(RdfTriple triple, string subjectIri, string predicateIri, string objectIri) =>
            triple.Subject is IriTerm s && s.Iri == subjectIri
            && triple.Predicate.Iri == predicateIri
            && triple.Object is IriTerm o && o.Iri == objectIri;
    }
}
