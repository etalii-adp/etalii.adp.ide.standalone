using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Turns the shapes projection into the core element vocabulary. Two of this reading's
/// structures are deliberately not elements and travel inside their card's payload instead:
/// target chips, because the data they name lives in another file, and constraint rows, because
/// they are written as blank nodes with no position to store (shacl-diagram Requirements 1.3,
/// 3.1).
/// </summary>
public sealed class ShaclElementMapper
{
    /// <summary>The mime-style kinds the canvas switches on.</summary>
    public const string ShapeType = "w3c/shacl+shape";

    /// <inheritdoc cref="ShapeType" />
    public const string EdgeType = "w3c/shacl+edge";

    /// <inheritdoc cref="ShapeType" />
    public const string TruncationType = "w3c/shacl+truncation";

    private readonly RdfElementMapper _family = new();

    /// <summary>The drawn shapes, plus the family truncation banner when the budget cut them.</summary>
    public IReadOnlyList<DiagramElement> Elements(
        ShaclProjectionResult projection,
        IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>();

        foreach (var card in projection.Cards)
        {
            var payload = new ShaclShapePayload
            {
                Iri = card.Iri,
                Display = card.Display,
                Blank = card.Blank,
                Deactivated = card.Deactivated,
                Severity = card.Severity,
                Closed = card.Closed,
                Name = card.Name,
                Description = card.Description,
            };

            foreach (var chip in card.Targets)
            {
                payload.Targets.Add(new ShaclTargetChipProto
                {
                    Kind = KindOf(chip.Kind),
                    TermDisplay = chip.TermDisplay,
                    TermIri = chip.TermIri,
                    DescribedInFile = chip.DescribedInFile,
                    ShapeIri = chip.ShapeIri,
                    PredicateIri = chip.PredicateIri,
                });
            }

            foreach (var row in card.Rows)
            {
                payload.Rows.Add(new ShaclPropertyRow
                {
                    Path = row.Path,
                    Name = row.Name,
                    Summary = row.Summary,
                    Cardinality = row.Cardinality,
                    Sparql = row.Sparql,
                    Blank = row.Blank,
                    Severity = row.Severity,
                });
            }

            elements.Add(Pack(card.Id, At(positions, card.Id), ShapeType, payload));
        }

        foreach (var edge in projection.Edges)
        {
            elements.Add(Pack(edge.Id, default, EdgeType, new ShaclEdgePayload
            {
                FromElementId = edge.FromId,
                ToElementId = edge.ToId,
                Kind = edge.Kind,
                Label = edge.Label,
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

    private static ShaclTargetKindProto KindOf(ShaclTargetKind kind) => kind switch
    {
        ShaclTargetKind.Class => ShaclTargetKindProto.Class,
        ShaclTargetKind.Node => ShaclTargetKindProto.Node,
        ShaclTargetKind.SubjectsOf => ShaclTargetKindProto.SubjectsOf,
        ShaclTargetKind.ObjectsOf => ShaclTargetKindProto.ObjectsOf,
        _ => ShaclTargetKindProto.ImplicitClass,
    };

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
