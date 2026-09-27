using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Judges a shapes-registered document: the family's text facts first, then what makes a shapes
/// file broken or self-contradictory (shacl-diagram Requirement 7) - and never what data does or
/// does not conform, because nothing here executes a shape.
/// </summary>
/// <remarks>
/// <para>
/// The family rules run through a composed <see cref="RdfValidator"/> rather than being restated:
/// core allows exactly one validator per origin, so a reading adds its rules by composition, not
/// by registering a second validator beside the family's. A file that does not parse is that one
/// finding and nothing else.
/// </para>
/// <para>
/// <b>What is deliberately not a finding.</b> A target naming a term this file does not describe
/// is the medium working as designed - a shapes graph aims at data that lives elsewhere
/// (Requirement 4.3) - so it is silent here, and a test pins that silence. Data conformance is
/// not judged at all: no data graph is loaded and no shape is run (Requirement 4.1).
/// </para>
/// <para>
/// <b>Why the reference finding is an info.</b> Requirement 7.5's rule fires when
/// <c>sh:node</c> or <c>sh:property</c> names an IRI this file does not describe. Info precisely
/// because, under the no-network rule, this tool cannot tell "missing" from "described in
/// another file" - a warning would be an accusation the evidence does not support, and the
/// honest report is that the reference leaves the file.
/// </para>
/// </remarks>
public sealed class ShaclValidator(DiagramOrigin origin) : IDiagramValidator
{
    /// <summary>A property shape without exactly one <c>sh:path</c> - the recommendation requires one.</summary>
    public const string PathCardinalityRuleId = "shacl.path-cardinality";

    /// <summary><c>sh:minCount</c> above <c>sh:maxCount</c>: nothing can conform.</summary>
    public const string ImpossibleCountsRuleId = "shacl.impossible-counts";

    /// <summary><c>sh:datatype</c> and <c>sh:class</c> on one shape: no value is both literal and instance.</summary>
    public const string DatatypeAndClassRuleId = "shacl.datatype-and-class";

    /// <summary>A term in the <c>sh:</c> namespace the recommendation does not define - the typo that silently disables a constraint.</summary>
    public const string UnknownTermRuleId = "shacl.unknown-term";

    /// <summary>A <c>sh:node</c> or <c>sh:property</c> reference leaving this file (Requirement 7.5).</summary>
    public const string ReferenceLeavesFileRuleId = "shacl.reference-leaves-file";

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

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        // Reason: Can still be null if the document is empty.
        var model = RdfParser.Parse(LineDocument.Parse(request.Document ?? ""));
        var shapes = ShaclShapeDiscovery.Discover(model);

        foreach (var shape in shapes)
        {
            var paths = model.Triples
                .Where(triple => ShaclShapeDiscovery.KeyOf(triple.Subject) == shape.Key
                    && triple.Predicate.Iri == ShaclVocabulary.Path)
                .ToList();

            // Only shapes used AS property shapes are required to have a path: a node shape has
            // none by definition, so silence there is correct rather than lenient.
            var usedAsProperty = model.Triples.Any(triple =>
                triple.Predicate.Iri == ShaclVocabulary.Property
                && ShaclShapeDiscovery.KeyOf(triple.Object) == shape.Key);

            if (usedAsProperty && paths.Count != 1)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    paths.Count == 0
                        ? $"{Display(model, shape)} is used as a property shape but states no sh:path; SHACL requires exactly one, and the row is drawn with an empty path."
                        : $"{Display(model, shape)} states {paths.Count} sh:path values; SHACL requires exactly one, so which values it constrains is undefined.",
                    PathCardinalityRuleId,
                    Location(paths.FirstOrDefault() ?? model.Triples.ElementAtOrDefault(shape.FirstTripleIndex))));
            }

            if (Number(model, shape.Key, ShaclVocabulary.MinCount) is { } min
                && Number(model, shape.Key, ShaclVocabulary.MaxCount) is { } max
                && min > max)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{Display(model, shape)} requires at least {min} values and at most {max}, so nothing can ever conform to it.",
                    ImpossibleCountsRuleId,
                    Location(Triple(model, shape.Key, ShaclVocabulary.MinCount))));
            }

            var datatype = Triple(model, shape.Key, ShaclVocabulary.Datatype);
            var classConstraint = Triple(model, shape.Key, ShaclVocabulary.Class);
            if (datatype is not null && classConstraint is not null)
            {
                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{Display(model, shape)} constrains its values by both sh:datatype and sh:class, and no value is both a literal and an instance of a class, so nothing can conform.",
                    DatatypeAndClassRuleId,
                    Location(datatype)));
            }
        }

        // A shape reference whose target this file never describes. Info, not warning: this tool
        // reads one file and cannot tell a missing shape from one defined next door
        // (Requirement 7.5). A TARGET naming an absent term is different and stays silent - that
        // is the medium working, not a loose end (Requirement 4.3).
        var described = model.Triples
            .Select(triple => ShaclShapeDiscovery.KeyOf(triple.Subject))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var triple in model.Triples)
        {
            if (triple.Predicate.Iri is not (ShaclVocabulary.Node or ShaclVocabulary.Property)
                || triple.Object is not IriTerm reference
                || described.Contains(ShaclShapeDiscovery.KeyOf(reference)))
            {
                continue;
            }

            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Info,
                $"{RdfProjection.Display(model, reference)} is referenced as a shape but not described in this file - it is defined elsewhere, or missing. Nothing outside the file is read to find out.",
                ReferenceLeavesFileRuleId,
                Location(triple)));
        }

        // The typo that silently disables a constraint: a sh: term the recommendation does not
        // define is read by no SHACL processor, so the constraint the author meant is simply absent.
        foreach (var triple in model.Triples)
        {
            foreach (var term in new[] { triple.Predicate.Iri, (triple.Object as IriTerm)?.Iri })
            {
                if (term is null
                    || !term.StartsWith(ShaclVocabulary.Namespace, StringComparison.Ordinal)
                    || ShaclVocabulary.AllTerms.Contains(term))
                {
                    continue;
                }

                problems.Add(new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"sh:{term[ShaclVocabulary.Namespace.Length..]} is not a term SHACL defines, so no processor will act on it - check the spelling.",
                    UnknownTermRuleId,
                    Location(triple)));
            }
        }

        return problems;
    }

    private static int? Number(RdfModel model, string shapeKey, string predicateIri) =>
        Triple(model, shapeKey, predicateIri)?.Object is LiteralTerm literal
        && int.TryParse(literal.Lexical, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static RdfTriple? Triple(RdfModel model, string shapeKey, string predicateIri) =>
        model.Triples.FirstOrDefault(triple =>
            ShaclShapeDiscovery.KeyOf(triple.Subject) == shapeKey && triple.Predicate.Iri == predicateIri);

    private static DiagramProblemLineLocation? Location(RdfTriple? triple) =>
        triple is null ? null : new DiagramProblemLineLocation((uint)(triple.Span.StartLine + 1));

    private static string Display(RdfModel model, ShaclShape shape) =>
        shape.Term is IriTerm term ? RdfProjection.Display(model, term) : "An anonymous shape";
}
