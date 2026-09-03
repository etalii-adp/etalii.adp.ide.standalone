using System.Text;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>What a rendered class expression came out as (owl-diagram Requirement 3).</summary>
/// <param name="Text">The Manchester-style text.</param>
/// <param name="Elided">Whether structure deeper than the depth cap was cut to <c>…</c> - the visible marker's flag (Requirement 3.4).</param>
/// <param name="Cyclic">Whether the structure references itself - rendered <c>…</c> at the revisit instead of recursing forever (Requirement 3.5).</param>
public sealed record OwlExpressionText(string Text, bool Elided, bool Cyclic);

/// <summary>
/// The Manchester-style rendering of a blank-node-rooted class expression: a pure recursive walk
/// over the structure's triples, depth-capped for canvas labels and uncapped for the property
/// grid's read-only text (owl-diagram Requirement 3).
/// </summary>
/// <remarks>
/// The uncapped form is computed per call - on selection, never for the whole file. A malformed
/// structure short-circuits to a label showing what it has, with <c>?</c> standing for what it
/// lacks; the projection carries the malformed flag beside it.
/// </remarks>
public static class ExpressionRenderer
{
    /// <summary>The canvas label's depth cap: structure nested deeper than two expression levels elides to <c>…</c>.</summary>
    public const int CanvasDepth = 2;

    /// <summary>Render <paramref name="root"/> against a model, capped at <paramref name="maxDepth"/> expression levels (null = uncapped).</summary>
    public static OwlExpressionText Render(RdfTerm root, RdfModel model, int? maxDepth = null) =>
        Render(root, OwlModelIndex.Build(model), model, maxDepth);

    internal static OwlExpressionText Render(RdfTerm root, OwlModelIndex index, RdfModel model, int? maxDepth)
    {
        var state = new RenderState(index, model, maxDepth ?? int.MaxValue);
        var text = state.Term(root, depth: 1);
        return new OwlExpressionText(text, state.Elided, state.Cyclic);
    }

    private sealed class RenderState(OwlModelIndex index, RdfModel model, int maxDepth)
    {
        private readonly HashSet<int> _path = [];

        public bool Elided { get; private set; }

        public bool Cyclic { get; private set; }

        public string Term(RdfTerm term, int depth)
        {
            switch (term)
            {
                case IriTerm iri:
                    return Display(iri.Iri);

                case LiteralTerm literal:
                    return literal.Lexical;

                case BlankTerm when depth > maxDepth:
                    Elided = true;
                    return "…";

                case BlankTerm blank when !_path.Add(blank.Ordinal):
                    Cyclic = true;
                    return "…";

                case BlankTerm blank:
                {
                    var text = Structure(blank, depth);
                    _path.Remove(blank.Ordinal);
                    return text;
                }

                default:
                    return "?";
            }
        }

        private string Structure(BlankTerm blank, int depth)
        {
            var about = index.TriplesOf(blank);

            foreach (var triple in about)
            {
                switch (triple.Predicate.Iri)
                {
                    case OwlVocabulary.UnionOf:
                        return $"({Members(triple.Object, depth, " ∪ ")})";
                    case OwlVocabulary.IntersectionOf:
                        return $"({Members(triple.Object, depth, " ∩ ")})";
                    case OwlVocabulary.ComplementOf:
                        return $"¬{Term(triple.Object, depth + 1)}";
                    case OwlVocabulary.OneOf:
                        return $"{{{Members(triple.Object, depth, ", ")}}}";
                }
            }

            // Not an operator: a restriction, well-formed or otherwise.
            var property = about.FirstOrDefault(t => t.Predicate.Iri == OwlVocabulary.OnProperty)?.Object;
            var propertyText = property is IriTerm propertyIri ? Display(propertyIri.Iri) : "?";

            foreach (var triple in about)
            {
                switch (triple.Predicate.Iri)
                {
                    case OwlVocabulary.SomeValuesFrom:
                        return $"∃ {propertyText}.{Term(triple.Object, depth + 1)}";
                    case OwlVocabulary.AllValuesFrom:
                        return $"∀ {propertyText}.{Term(triple.Object, depth + 1)}";
                    case OwlVocabulary.HasValue:
                        return $"∋ {propertyText}.{Term(triple.Object, depth + 1)}";
                    case OwlVocabulary.Cardinality:
                        return Cardinality("=", triple.Object, propertyText, filler: null, depth);
                    case OwlVocabulary.MinCardinality:
                        return Cardinality("≥", triple.Object, propertyText, filler: null, depth);
                    case OwlVocabulary.MaxCardinality:
                        return Cardinality("≤", triple.Object, propertyText, filler: null, depth);
                    case OwlVocabulary.QualifiedCardinality:
                        return Cardinality("=", triple.Object, propertyText, Filler(about), depth);
                    case OwlVocabulary.MinQualifiedCardinality:
                        return Cardinality("≥", triple.Object, propertyText, Filler(about), depth);
                    case OwlVocabulary.MaxQualifiedCardinality:
                        return Cardinality("≤", triple.Object, propertyText, Filler(about), depth);
                }
            }

            // No quantifier at all: show what there is, ? standing for what is missing.
            return $"? {propertyText}";
        }

        private static RdfTerm? Filler(IReadOnlyList<RdfTriple> about) =>
            about.FirstOrDefault(t => t.Predicate.Iri is OwlVocabulary.OnClass or OwlVocabulary.OnDataRange)?.Object;

        private string Cardinality(string comparator, RdfTerm count, string propertyText, RdfTerm? filler, int depth)
        {
            var countText = count is LiteralTerm literal ? literal.Lexical : "?";
            var head = $"{comparator} {countText} {propertyText}";
            return filler is null ? head : $"{head}.{Term(filler, depth + 1)}";
        }

        private string Members(RdfTerm head, int depth, string separator)
        {
            var parts = new StringBuilder();
            var cursor = head;
            while (cursor is BlankTerm cell && index.Firsts.TryGetValue(cell.Ordinal, out var member))
            {
                if (parts.Length > 0)
                {
                    parts.Append(separator);
                }

                parts.Append(Term(member, depth + 1));
                cursor = index.Rests.TryGetValue(cell.Ordinal, out var next) ? next : new IriTerm(RdfVocabulary.Nil, "rdf:nil");
            }

            return parts.Length == 0 ? "?" : parts.ToString();
        }

        private string Display(string iri) =>
            index.Labels.TryGetValue(iri, out var label) ? label : RdfProjection.Display(model, new IriTerm(iri, ""));
    }
}
