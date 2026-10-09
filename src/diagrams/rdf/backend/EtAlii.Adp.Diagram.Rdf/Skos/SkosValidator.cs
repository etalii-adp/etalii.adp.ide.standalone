using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Judges a scheme-registered document: the family's text facts first, then what makes a
/// vocabulary lie (skos-diagram Requirement 7) - all by assertion, no inference, no network.
/// </summary>
/// <remarks>
/// <para>
/// The family rules run through a composed <see cref="RdfValidator"/> rather than being
/// restated: one implementation of the text facts, answering under whichever origin registered
/// the file. A file that does not parse is that one finding and nothing else.
/// </para>
/// <para>
/// The cycle finding consumes <see cref="SkosLayout"/>'s own detection - never a second
/// detector - so the drawn break and the reported cycle cannot disagree (Requirement 7.1). The
/// S27 related-versus-hierarchy check covers the DIRECT case only: the transitive case requires
/// entailment, which the no-inference rule forbids, so it is not checked badly - it is not
/// checked, and this sentence is where that boundary is recorded.
/// </para>
/// </remarks>
public sealed class SkosValidator(DiagramOrigin origin) : IDiagramValidator
{
    /// <summary>An asserted broader/narrower cycle - drawn whole, layered by exclusion, reported here.</summary>
    public const string CycleRuleId = "skos.hierarchy-cycle";

    /// <summary>More than one <c>skos:prefLabel</c> in one language (SKOS S14).</summary>
    public const string DuplicatePrefLabelRuleId = "skos.duplicate-preflabel";

    /// <summary><c>skos:related</c> between directly hierarchy-linked concepts (S27's direct case).</summary>
    public const string RelatedOverlapRuleId = "skos.related-overlap";

    /// <summary>A concept with no <c>skos:prefLabel</c> in any language.</summary>
    public const string MissingPrefLabelRuleId = "skos.missing-preflabel";

    /// <summary>A concept asserted into no scheme and reachable from no top concept - legal SKOS, and usually a mistake.</summary>
    public const string UnfiledConceptRuleId = "skos.unfiled-concept";

    /// <summary>SKOS-XL label triples are present and deliberately unread (Requirement 3.6).</summary>
    public const string XlLabelsRuleId = "skos.xl-labels";

    /// <summary>A hierarchy or membership assertion whose concept end is not an asserted <c>skos:Concept</c>.</summary>
    public const string NonConceptRuleId = "skos.nonconcept-target";

    /// <summary>A <c>language:</c> header above a core header it would sever - ignored, and named here.</summary>
    public const string MisplacedHeaderRuleId = "skos.misplaced-header";

    private readonly RdfValidator _family = new(origin);

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = new List<DiagramProblem>(await _family.ValidateAsync(request, cancellationToken));
        if (problems.Any(problem => problem.RuleId == RdfValidator.UnparseableRuleId))
        {
            // One finding, never buried under consequences (family Requirement 7.1).
            return problems;
        }

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - Reason: IDiagramValidator.ValidateAsync is a public module seam and the record's non-nullable Document is a compile-time annotation the runtime does not enforce, so a caller that passes null (null!, or code built without nullable checks) gets an empty document here rather than a NullReferenceException; ProjectValidator, the one caller in this repository, always passes the text it read, so an empty file arrives as "", not null.
        var model = RdfParser.Parse(LineDocument.Parse(request.Document ?? ""));
        var projection = SkosProjection.Project(model);
        var layout = SkosLayout.Layout(projection);

        foreach (var cycle in layout.Cycles)
        {
            var names = string.Join(", ", cycle.ConceptIds.Select(Display));
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"The broader/narrower hierarchy runs in a circle through {names}. The diagram draws every edge and breaks the circle for layering only.",
                CycleRuleId,
                Location(cycle.Triples.FirstOrDefault())));
        }

        // The file's own typing assertions, never the projection's concept list: that list is
        // cut to the drawing budget, and a concept past the cut is still typed (the rule's
        // message promises "the file's own silence", so the file is what it must read).
        var conceptIds = SkosProjection.TypedIds(model, SkosVocabulary.Concept);
        foreach (var concept in projection.Concepts)
        {
            foreach (var group in concept.Labels
                .Where(label => label.Source == SkosLabelSource.Preferred)
                .GroupBy(label => label.Language ?? "")
                .Where(group => group.Count() > 1))
            {
                var tag = group.Key.Length == 0 ? "no language tag" : $"language '{group.Key}'";
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"{Display(concept.Id)} has {group.Count()} preferred labels with {tag}; SKOS allows one (S14). The lexicographically first is drawn.",
                    DuplicatePrefLabelRuleId,
                    Location(group.First().Triple)));
            }

            if (concept.Labels.All(label => label.Source != SkosLabelSource.Preferred))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{Display(concept.Id)} has no skos:prefLabel in any language, so the diagram falls back to {(concept.Labels.Count > 0 ? "an alternate label" : "its IRI")}.",
                    MissingPrefLabelRuleId));
            }
        }

        var hierarchyPairs = projection.Edges
            .Where(edge => edge.Kind == SkosEdgeKind.Hierarchy)
            .Select(edge => Unordered(edge.FromId, edge.ToId))
            .ToHashSet();
        foreach (var related in projection.Edges.Where(edge => edge.Kind == SkosEdgeKind.Related))
        {
            if (hierarchyPairs.Contains(Unordered(related.FromId, related.ToId)))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{Display(related.FromId)} and {Display(related.ToId)} are both hierarchy-linked and skos:related, which SKOS S27 forbids. Only this direct case is checked: the transitive case would need inference, which this diagram never does.",
                    RelatedOverlapRuleId,
                    Location(related.Triples.FirstOrDefault())));
            }
        }

        ReportUnfiled(projection, problems);
        ReportNonConceptEnds(model, conceptIds, problems);

        if (SkosProjection.HasXlLabels(model))
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Info,
                "This file labels concepts through SKOS-XL, which this reading does not resolve; the plain-SKOS fallbacks apply (Requirement 3.6).",
                XlLabelsRuleId));
        }

        (_, int misplacedLine) = SkosRegistrationLanguage.Read(request.RegistrationPath);
        if (misplacedLine > 0)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The registration's language: header (line {misplacedLine}) sits above a body: or view: line, where core's header scan would stop before reaching them. It is ignored; move it below body:/view: and above layout:.",
                MisplacedHeaderRuleId));
        }

        return problems;

        string Display(string elementId)
        {
            var concept = projection.Concepts.FirstOrDefault(c => c.Id == elementId);
            return concept is not null
                ? SkosLabels.Choose(concept.Labels, SkosLabels.DefaultLanguage, Short(concept.Iri)).Text
                : Short(elementId.StartsWith("res:", StringComparison.Ordinal) ? elementId["res:".Length..] : elementId);
        }
    }

    private void ReportUnfiled(SkosProjectionResult projection, List<DiagramProblem> problems)
    {
        // Reachability over the drawn hierarchy from every asserted top concept - projection of
        // asserted edges, not entailment.
        var childrenOf = projection.Edges
            .Where(edge => edge.Kind == SkosEdgeKind.Hierarchy)
            .GroupBy(edge => edge.FromId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToId).ToList(), StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>(projection.Schemes.SelectMany(scheme => scheme.TopConceptIds));
        while (frontier.TryDequeue(out var id))
        {
            if (!reached.Add(id) || !childrenOf.TryGetValue(id, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                frontier.Enqueue(child);
            }
        }

        foreach (var concept in projection.Concepts)
        {
            if (concept.SchemeIris.Count == 0 && !reached.Contains(concept.Id))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Info,
                    $"{Short(concept.Iri)} is in no concept scheme and under no top concept - legal SKOS, and usually a mistake. It draws in the unfiled band.",
                    UnfiledConceptRuleId));
            }
        }
    }

    private static void ReportNonConceptEnds(RdfModel model, HashSet<string> conceptIds, List<DiagramProblem> problems)
    {
        foreach (var triple in model.Triples)
        {
            RdfTerm[]? check = triple.Predicate.Iri switch
            {
                SkosVocabulary.Broader or SkosVocabulary.Narrower or SkosVocabulary.Related =>
                    new[] { triple.Subject, triple.Object },
                SkosVocabulary.InScheme or SkosVocabulary.TopConceptOf => [triple.Subject],
                SkosVocabulary.HasTopConcept => [triple.Object],
                _ => null,
            };
            if (check is null)
            {
                continue;
            }

            foreach (var term in check)
            {
                var id = SkosProjection.ElementId(term);
                if (id is not null && !conceptIds.Contains(id))
                {
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Warning,
                        $"{Short(id)} takes part in a {RdfProjection.Display(model, triple.Predicate)} assertion but is not typed skos:Concept. Typing is never inferred, so only the file's own silence is reported.",
                        NonConceptRuleId,
                        Location(triple)));
                }
            }
        }
    }

    private static DiagramProblemLineLocation? Location(RdfTriple? triple) =>
        triple is null ? null : new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1));

    private static (string, string) Unordered(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    private static string Short(string iri)
    {
        if (iri.StartsWith("res:", StringComparison.Ordinal))
        {
            iri = iri["res:".Length..];
        }

        var cut = Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/'));
        return cut >= 0 && cut < iri.Length - 1 ? iri[(cut + 1)..] : iri;
    }
}
