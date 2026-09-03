using Serilog;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Judges a document read as an ontology (owl-diagram Requirement 5): structural facts about the
/// asserted OWL, no inference, no network. The anchor's file-level rules (parse failures, prefix
/// redeclarations, datatype and language-tag facts) are NOT restated here - the family registers
/// the anchor's validator beside this one for the ontology origin, so they arrive from the shared
/// engine (Requirement 5.5).
/// </summary>
public sealed class OwlValidator(DiagramOrigin origin) : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<OwlValidator>();

    /// <summary>The rule reported when classes assert each other as subclasses in a cycle.</summary>
    public const string SubclassCycleRuleId = "owl.subclass-cycle";

    /// <summary>The rule reported for a class expression whose structure is broken.</summary>
    public const string MalformedExpressionRuleId = "owl.malformed-expression";

    /// <summary>The rule reported for an IRI used in a property's axiom position but declared nowhere in the file.</summary>
    public const string UndeclaredPropertyRuleId = "owl.undeclared-property";

    /// <summary>The rule reported when a non-deprecated axiom references a deprecated entity.</summary>
    public const string DeprecatedReferenceRuleId = "owl.deprecated-reference";

    /// <summary>The rule reported - once per import - naming what is deliberately not fetched.</summary>
    public const string ImportsNotFetchedRuleId = "owl.imports-not-fetched";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RdfModel model;
        try
        {
            model = RdfParser.Parse(RdfDocument.Parse(request.Document ?? ""));
        }
        catch (RdfParseException exception)
        {
            // The anchor's validator, registered beside this one, reports the parse failure;
            // reporting it twice would say less, not more.
            _logger.Debug(exception, "{BaseName} does not parse; leaving the finding to the family validator", request.BaseName);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
        }

        return ValueTask.FromResult(Judge(model));
    }

    /// <summary>The Requirement 5 findings over a parsed model - a pure function, tested as one.</summary>
    internal static IReadOnlyList<DiagramProblem> Judge(RdfModel model)
    {
        // NOTE: three of these findings are specified as INFO-level (undeclared property,
        // deprecated reference, imports not fetched), but the problems pipeline currently knows
        // only Error and Warning; they ship as warnings until an Info severity exists end to end.
        var problems = new List<DiagramProblem>();
        var index = OwlModelIndex.Build(model);

        // Subclass cycles: legal OWL - a cycle asserts mutual equivalence - and almost always an
        // authoring mistake, which is exactly what a warning is for (Requirement 5.1).
        var supers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri == OwlVocabulary.SubClassOf
                && triple.Subject is IriTerm child && triple.Object is IriTerm parent)
            {
                Adjacency(child.Iri).Add(parent.Iri);
                _ = Adjacency(parent.Iri);
            }
        }

        foreach (var cycle in OwlClassHierarchy.Cycles(supers))
        {
            var names = string.Join(", ", cycle.Select(iri => RdfProjection.Display(model, new IriTerm(iri, ""))));
            var line = model.Triples.First(t =>
                t.Predicate.Iri == OwlVocabulary.SubClassOf
                && t.Subject is IriTerm s && cycle.Contains(s.Iri)).Span.StartLine;
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"These classes assert each other as subclasses in a cycle: {names}. That is legal OWL - it makes them mutually equivalent - and it is almost always a mistake.",
                SubclassCycleRuleId,
                new DiagramProblemLineLocation((uint)(line + 1))));
        }

        // Malformed class expressions, from the projection's own judgment (Requirement 5.2).
        var graph = OwlProjection.Project(model, int.MaxValue);
        foreach (var node in graph.Nodes.Where(node => node.Malformed))
        {
            var line = node.ExpressionRoot is BlankTerm root
                ? model.Triples.FirstOrDefault(t => t.Subject is BlankTerm b && b.Ordinal == root.Ordinal)?.Span.StartLine ?? 0
                : 0;
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"This class expression is not well-formed: it renders as '{node.Display}', with ? standing for what the structure is missing.",
                MalformedExpressionRuleId,
                new DiagramProblemLineLocation((uint)(line + 1))));
        }

        // An IRI used in a property's axiom position but declared nowhere here - often a term
        // living in an import, worth naming rather than erroring under the no-fetch stance
        // (Requirement 5.3).
        var declaredProperties = index.ObjectProperties
            .Concat(index.DataProperties)
            .Concat(index.AnnotationProperties)
            .ToHashSet(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            var candidate = triple.Predicate.Iri switch
            {
                OwlVocabulary.Domain or OwlVocabulary.Range when triple.Subject is IriTerm subject => subject.Iri,
                OwlVocabulary.OnProperty when triple.Object is IriTerm target => target.Iri,
                _ => null,
            };
            if (candidate is null || OwlVocabulary.IsBuiltIn(candidate)
                || declaredProperties.Contains(candidate) || !reported.Add(candidate))
            {
                continue;
            }

            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"{RdfProjection.Display(model, new IriTerm(candidate, ""))} is used as a property but declared nowhere in this file - it may live in an import, which is deliberately not fetched.",
                UndeclaredPropertyRuleId,
                new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1))));
        }

        // A non-deprecated axiom referencing a deprecated entity (Requirement 5.4).
        var deprecatedReported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri == OwlVocabulary.Deprecated || triple.Predicate.Iri == RdfVocabulary.Type)
            {
                continue;
            }

            if (triple.Object is IriTerm target
                && index.Deprecated.Contains(target.Iri)
                && (triple.Subject is not IriTerm subject || !index.Deprecated.Contains(subject.Iri))
                && deprecatedReported.Add(target.Iri))
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{RdfProjection.Display(model, new IriTerm(target.Iri, ""))} is deprecated, and something not deprecated still references it.",
                    DeprecatedReferenceRuleId,
                    new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1))));
            }
        }

        // Imports are named, never fetched: the drawn ontology is this file alone
        // (Requirement 4.2, the no-network no-inference rule made visible).
        foreach (var triple in model.Triples.Where(t => t.Predicate.Iri == OwlVocabulary.Imports))
        {
            if (triple.Object is IriTerm import)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"This ontology imports {import.Iri}, which is not fetched: the diagram shows what this file asserts, not the import closure a reasoner would load.",
                    ImportsNotFetchedRuleId,
                    new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1))));
            }
        }

        return problems;

        List<string> Adjacency(string iri)
        {
            if (!supers.TryGetValue(iri, out var list))
            {
                list = [];
                supers[iri] = list;
            }

            return list;
        }
    }
}
