using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
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
    string BodyPath, string ConceptIri, string PrefLabel, string Language, string SchemeIri) : ICommand;

/// <summary>
/// Removes every asserted triple of one hierarchy or related pair - a both-ways pair goes as one
/// command, stated in the confirmation (skos-diagram Requirement 5.6).
/// </summary>
/// <param name="BodyPath">The SKOS file.</param>
/// <param name="AIri">One end - the canonical id's first IRI (the narrower end for hierarchy).</param>
/// <param name="BIri">The other end.</param>
/// <param name="Hierarchy">True for a broader/narrower pair, false for related.</param>
public sealed record DisconnectSkosPairCommand(
    string BodyPath, string AIri, string BIri, bool Hierarchy) : ICommand;

/// <inheritdoc cref="AddSkosConceptCommand" />
public sealed class AddSkosConceptCommandHandler(IRdfDocumentStore documents) : ICommandHandler<AddSkosConceptCommand>
{
    public Task<CommandResult> ExecuteAsync(AddSkosConceptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
        {
            // Three splices, one snapshot inverse. The writer's coordinates are valid only for
            // the parse they came from, so the model is re-read between splices rather than
            // trusted across them (the family's own stale-span rule).
            var refusal = RdfWriter.AddTriple(
                entry.Document, entry.Model,
                command.ConceptIri, RdfVocabulary.Type, new IriTerm(SkosVocabulary.Concept, ""));
            if (refusal.Length > 0)
            {
                return refusal;
            }

            var model = RdfParser.Parse(entry.Document);
            refusal = RdfWriter.AddTriple(
                entry.Document, model,
                command.ConceptIri, SkosVocabulary.PrefLabel,
                new LiteralTerm(command.PrefLabel, null, command.Language.Length > 0 ? command.Language : null, ""));
            if (refusal.Length > 0)
            {
                return refusal;
            }

            if (command.SchemeIri.Length > 0)
            {
                model = RdfParser.Parse(entry.Document);
                refusal = RdfWriter.AddTriple(
                    entry.Document, model,
                    command.ConceptIri, SkosVocabulary.InScheme, new IriTerm(command.SchemeIri, ""));
                if (refusal.Length > 0)
                {
                    return refusal;
                }
            }

            return "";
        });
    }
}

/// <inheritdoc cref="DisconnectSkosPairCommand" />
public sealed class DisconnectSkosPairCommandHandler(IRdfDocumentStore documents) : ICommandHandler<DisconnectSkosPairCommand>
{
    public Task<CommandResult> ExecuteAsync(DisconnectSkosPairCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return RdfEdits.Run(documents, command.BodyPath, command, entry =>
        {
            // One asserted direction or both: each splice invalidates the previous parse's
            // coordinates, so the pair is re-found on a fresh model until none remains.
            var removed = 0;
            while (true)
            {
                var model = removed == 0 ? entry.Model : RdfParser.Parse(entry.Document);
                var triple = (command.Hierarchy
                        ? SkosPair.Hierarchy(model, command.AIri, command.BIri)
                        : SkosPair.Related(model, command.AIri, command.BIri))
                    .FirstOrDefault();
                if (triple is null)
                {
                    return removed > 0
                        ? ""
                        : "That connection is not in the file as it stands, so there is nothing to disconnect.";
                }

                var refusal = RdfWriter.RemoveTriple(entry.Document, model, triple);
                if (refusal.Length > 0)
                {
                    return refusal;
                }

                removed++;
            }
        });
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
