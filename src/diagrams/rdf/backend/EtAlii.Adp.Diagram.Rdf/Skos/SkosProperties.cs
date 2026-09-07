using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The property grid of an element the file asserts to be a concept, scheme or collection
/// (skos-diagram Requirement 6.2-6.3): identity and notation, every label by kind and language,
/// the documentation properties as editable rows, memberships, and out-of-file mappings
/// read-only with their reason. Label and documentation edits are single-literal rewrites -
/// exactly what the family's <c>ReplaceObjectLiteral</c> exists for.
/// </summary>
public static class SkosProperties
{
    private const string IdentityGroup = "Identity";
    private const string LabelsGroup = "Labels";
    private const string DocumentationGroup = "Documentation";
    private const string MembershipGroup = "Membership";
    private const string MappingsGroup = "Mappings";

    private const string RenameViaMenu = "Rename through the context menu, so every reference follows the name.";
    private const string MembershipIsGesture = "Membership is stated by triples; file and unfile concepts on the canvas.";
    private const string XlBoundary = "This concept's labels are stated through SKOS-XL, which this reading does not resolve (Requirement 3.6). Edit them as triples in the graph reading.";
    private const string OutOfFileMapping = "The mapped concept lives outside this file, so the mapping shows here rather than drawing.";

    /// <summary>The skos rows for the selection, or null when the element is not this reading's business.</summary>
    public static IReadOnlyList<ContextPropertyDefinition>? Describe(RdfDocumentEntry entry, ContextTarget target)
    {
        var conceptIri = SkosSelection.ConceptOf(entry, target.ElementId);
        var containerIri = conceptIri ?? SkosSelection.SchemeOrCollectionOf(entry, target.ElementId);
        if (containerIri is null)
        {
            return null;
        }

        var truncated = RdfSelection.IsTruncated(entry);
        var xl = SkosSelection.HasXlLabels(entry, containerIri);
        var rows = new List<ContextPropertyDefinition>
        {
            new("skos.iri", "IRI", containerIri, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
        };

        var notations = Literals(entry, containerIri, SkosVocabulary.Notation).ToList();
        if (notations.Count > 0)
        {
            rows.Add(new ContextPropertyDefinition(
                "skos.notation", "Notation", string.Join(", ", notations.Select(l => l.Lexical)),
                ReadOnlyReason: "Notations are codes other systems key off; edit them as triples.", Group: IdentityGroup));
        }

        foreach (var (source, name) in new[]
        {
            (SkosVocabulary.PrefLabel, "Preferred"),
            (SkosVocabulary.AltLabel, "Alternate"),
            (SkosVocabulary.HiddenLabel, "Hidden"),
        })
        {
            foreach (var literal in Literals(entry, containerIri, source))
            {
                var tag = literal.Language is { Length: > 0 } language ? $" @{language}" : "";
                var editable = source == SkosVocabulary.PrefLabel && !truncated && !xl;
                rows.Add(new ContextPropertyDefinition(
                    $"skos.label:{Fragment(source)}:{literal.Language ?? ""}",
                    $"{name}{tag}",
                    literal.Lexical,
                    ReadOnlyReason: editable ? "" : xl ? XlBoundary : truncated ? RdfSelection.TruncatedRefusal : "Alternate and hidden labels are edited as triples.",
                    Group: LabelsGroup));
            }
        }

        foreach (var documentation in SkosVocabulary.Documentation)
        {
            var literal = Literals(entry, containerIri, documentation).FirstOrDefault();
            rows.Add(new ContextPropertyDefinition(
                $"skos.doc:{Fragment(documentation)}",
                Capitalize(Fragment(documentation)),
                literal?.Lexical ?? "",
                ReadOnlyReason: truncated ? RdfSelection.TruncatedRefusal : "",
                Group: DocumentationGroup));
        }

        if (conceptIri is not null)
        {
            var schemes = entry.Model.Triples
                .Where(t => t.Subject is IriTerm s && s.Iri == conceptIri
                    && t.Predicate.Iri is SkosVocabulary.InScheme or SkosVocabulary.TopConceptOf
                    && t.Object is IriTerm)
                .Select(t => RdfProjection.Display(entry.Model, (IriTerm)t.Object))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            rows.Add(new ContextPropertyDefinition(
                "skos.schemes", "In schemes", schemes.Count > 0 ? string.Join(", ", schemes) : "(unfiled)",
                ReadOnlyReason: MembershipIsGesture, Group: MembershipGroup));

            foreach (var mapping in entry.Model.Triples.Where(t =>
                t.Subject is IriTerm s && s.Iri == conceptIri
                && SkosVocabulary.Mappings.Contains(t.Predicate.Iri)
                && t.Object is IriTerm o
                && SkosSelection.ConceptOf(entry, $"res:{o.Iri}") is null))
            {
                rows.Add(new ContextPropertyDefinition(
                    $"skos.mapping:{Fragment(mapping.Predicate.Iri)}:{((IriTerm)mapping.Object).Iri}",
                    Capitalize(Fragment(mapping.Predicate.Iri)),
                    ((IriTerm)mapping.Object).Iri,
                    ReadOnlyReason: OutOfFileMapping,
                    Group: MappingsGroup));
            }
        }

        return rows;
    }

    /// <summary>The command a skos row edit dispatches, or null when the row is not this reading's or not editable.</summary>
    public static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string propertyId, string value)
    {
        var iri = SkosSelection.ConceptOf(entry, target.ElementId)
            ?? SkosSelection.SchemeOrCollectionOf(entry, target.ElementId);
        if (iri is null)
        {
            return null;
        }

        if (propertyId.StartsWith("skos.label:prefLabel:", StringComparison.Ordinal))
        {
            if (SkosSelection.HasXlLabels(entry, iri))
            {
                return null; // Refused with the XL sentence at describe time; never spliced.
            }

            var language = propertyId["skos.label:prefLabel:".Length..];
            var existing = Literals(entry, iri, SkosVocabulary.PrefLabel)
                .FirstOrDefault(l => (l.Language ?? "") == language);
            return existing is null
                ? new AddRdfTripleCommand(target.ResolvedFullPath, iri, SkosVocabulary.PrefLabel, "", value, language)
                : Replace(target, iri, SkosVocabulary.PrefLabel, existing, value);
        }

        if (propertyId.StartsWith("skos.doc:", StringComparison.Ordinal))
        {
            var fragment = propertyId["skos.doc:".Length..];
            var predicate = SkosVocabulary.Documentation.FirstOrDefault(d => Fragment(d) == fragment);
            if (predicate is null)
            {
                return null;
            }

            var existing = Literals(entry, iri, predicate).FirstOrDefault();
            return existing is null
                ? new AddRdfTripleCommand(target.ResolvedFullPath, iri, predicate, "", value)
                : Replace(target, iri, predicate, existing, value);
        }

        return null;
    }

    private static ReplaceRdfObjectLiteralCommand Replace(
        ContextTarget target, string iri, string predicateIri, LiteralTerm existing, string value) =>
        // The language tag survives by construction: only the lexical token is rewritten
        // (Requirement 5.4, over the family's ReplaceObjectLiteral).
        new(target.ResolvedFullPath, iri, predicateIri,
            existing.Lexical, existing.Language ?? "", existing.DatatypeIri ?? "",
            value, existing.Language ?? "", existing.DatatypeIri ?? "");

    private static IEnumerable<LiteralTerm> Literals(RdfDocumentEntry entry, string iri, string predicateIri) =>
        entry.Model.Triples
            .Where(t => t.Subject is IriTerm s && s.Iri == iri && t.Predicate.Iri == predicateIri)
            .Select(t => t.Object)
            .OfType<LiteralTerm>();

    private static string Fragment(string iri)
    {
        var cut = Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/'));
        return cut >= 0 && cut < iri.Length - 1 ? iri[(cut + 1)..] : iri;
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
