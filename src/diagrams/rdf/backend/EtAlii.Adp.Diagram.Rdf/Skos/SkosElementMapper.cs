using EtAlii.Adp.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Turns the scheme projection into the core element vocabulary. Labels arrive already chosen -
/// <see cref="SkosLabels.Choose"/> runs here once per element, and the canvas never recomputes a
/// name (skos-diagram Requirement 3).
/// </summary>
public sealed class SkosElementMapper
{
    /// <summary>The mime-style kinds the canvas switches on.</summary>
    public const string ConceptType = "w3c/skos+concept";

    /// <inheritdoc cref="ConceptType" />
    public const string SchemeType = "w3c/skos+scheme";

    /// <inheritdoc cref="ConceptType" />
    public const string CollectionType = "w3c/skos+collection";

    /// <inheritdoc cref="ConceptType" />
    public const string EdgeType = "w3c/skos+edge";

    /// <inheritdoc cref="ConceptType" />
    public const string TruncationType = "w3c/skos+truncation";

    /// <summary>The drawn vocabulary, plus the family truncation banner when the budget cut it.</summary>
    public IReadOnlyList<DiagramElement> Elements(
        SkosProjectionResult projection,
        IReadOnlyDictionary<string, RegistrationPosition> positions,
        string displayLanguage)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLanguage);

        var elements = new List<DiagramElement>();

        foreach (var scheme in projection.Schemes)
        {
            var chosen = Chosen(scheme.Labels, displayLanguage, scheme.Iri);
            elements.Add(Pack(scheme.Id, At(positions, scheme.Id), SchemeType, new SkosSchemePayload
            {
                Iri = scheme.Iri,
                Label = chosen.Text,
                LanguageTag = chosen.LanguageTag,
                LabelKind = (int)chosen.Kind,
                MemberCount = projection.Concepts.Count(concept => concept.SchemeIris.Contains(scheme.Iri)),
            }));
        }

        foreach (var concept in projection.Concepts)
        {
            var chosen = Chosen(concept.Labels, displayLanguage, concept.Iri);
            elements.Add(Pack(concept.Id, At(positions, concept.Id), ConceptType, new SkosConceptPayload
            {
                Iri = concept.Iri,
                Label = chosen.Text,
                LanguageTag = chosen.LanguageTag,
                LabelKind = (int)chosen.Kind,
                Notation = concept.Notations.Count > 0 ? concept.Notations[0] : "",
                SchemeIris = { concept.SchemeIris },
                Blank = concept.Blank,
                // The comparison lives here because only the session knows its display
                // language; an untagged label is language-neutral and wears no chip.
                LanguageChip = chosen.LanguageTag.Length > 0 && chosen.LanguageTag != displayLanguage,
            }));
        }

        foreach (var collection in projection.Collections)
        {
            var chosen = Chosen(collection.Labels, displayLanguage, collection.Iri);
            elements.Add(Pack(collection.Id, At(positions, collection.Id), CollectionType, new SkosCollectionPayload
            {
                Iri = collection.Iri,
                Label = chosen.Text,
                LanguageTag = chosen.LanguageTag,
                LabelKind = (int)chosen.Kind,
                Ordered = collection.Ordered,
                MemberIds = { collection.MemberIds },
            }));
        }

        foreach (var edge in projection.Edges)
        {
            elements.Add(Pack(edge.Id, default, EdgeType, new SkosEdgePayload
            {
                FromElementId = edge.FromId,
                ToElementId = edge.ToId,
                Kind = (int)edge.Kind,
                Predicate = edge.Kind == SkosEdgeKind.Mapping ? Shorten(edge.PredicateIri) : "",
                AssertedBothWays = edge.AssertedBothWays,
            }));
        }

        if (projection.Truncated)
        {
            elements.Add(Pack(RdfElementMapper.TruncationId, At(positions, RdfElementMapper.TruncationId), TruncationType, new RdfTruncationPayload
            {
                Shown = projection.Shown,
                Total = projection.Total,
            }));
        }

        return elements;
    }

    /// <summary>The family diff, reused verbatim - one delta vocabulary for every reading.</summary>
    public IReadOnlyList<DiagramDelta> Diff(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after) => _family.Diff(before, after);

    private readonly RdfElementMapper _family = new();

    private static SkosChosenLabel Chosen(IReadOnlyList<SkosLabel> labels, string displayLanguage, string iri) =>
        SkosLabels.Choose(labels, displayLanguage, Shorten(iri));

    /// <summary>A short display for an IRI with no label: its fragment or last path segment.</summary>
    private static string Shorten(string iri)
    {
        if (iri.Length == 0)
        {
            return "";
        }

        var cut = Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/'));
        return cut >= 0 && cut < iri.Length - 1 ? iri[(cut + 1)..] : iri;
    }

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
