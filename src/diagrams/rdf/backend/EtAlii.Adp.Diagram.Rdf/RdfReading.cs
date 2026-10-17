namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The data-graph reading: every resource a card, every triple between them an edge (rdf-diagram Requirements 2-5).</summary>
/// <remarks>
/// This is the reading where viewport filtering pays for itself: the drawn-node budget alone
/// discards by document order, so a reader of a large ontology could never reach past the first
/// thousand resources however far they panned. The budget survives as a floor against a
/// pathological view (Requirement 5.4), not as the means of keeping the document drawable.
/// </remarks>
public sealed class RdfReading(RdfElementMapper mapper) : IRdfReading<RdfProjectionResult>
{
    /// <inheritdoc />
    public string ParentRefusal => "A resource has no parent to move it under; dragging changes where it sits on the canvas.";

    /// <inheritdoc />
    public RdfProjectionResult Project(RdfModel model) => RdfProjection.Project(model, int.MaxValue);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, RegistrationPosition> Positions(
        RdfProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> stored) =>
        RegistrationLayout.Apply(RdfLayout.Positions(projection), stored);

    /// <inheritdoc />
    /// <remarks>
    /// The counts handed on are the <em>document's</em>, not this view's, and that is deliberate.
    /// <see cref="RdfSelection.IsTruncated" /> decides read-only from the document alone, with no
    /// viewport to consult, so a banner derived from the view would contradict it - a file could
    /// be read-only with nothing on screen saying why, or say it is truncated while every edit
    /// was allowed. Reach improves; what a document may do does not change.
    /// </remarks>
    public RdfProjectionResult InView(
        RdfProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var visible = projection.Nodes
            .Where(node => RdfViewport.Admits(viewport, positions, node.Id, RdfLayout.CellWidth, RdfLayout.CellHeight))
            .ToList();

        // The floor: only a view holding more than the whole budget is cut, and then in document
        // order, so the same view always yields the same nodes.
        var truncated = projection.Total > RdfProjection.DefaultBudget;
        var kept = visible.Count > RdfProjection.DefaultBudget
            ? visible.Take(RdfProjection.DefaultBudget).ToList()
            : visible;

        var keptIds = kept.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new RdfProjectionResult(
            kept,
            edges,
            truncated ? RdfProjection.DefaultBudget : kept.Count,
            truncated ? projection.Total : kept.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramElement> Elements(
        RdfProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions) =>
        mapper.Elements(projection, positions);

    /// <inheritdoc />
    public string? Refusal(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == RdfElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        // The identity boundary: a blank node's ordinal is this parse's alone, so a stored
        // position would silently jump to another node on the next reparse (Requirement 4.5).
        return elementId.StartsWith("blank:", StringComparison.Ordinal)
            ? "That is a blank node, whose identity does not survive a reparse, so a stored position could not be trusted. Name it with an IRI to arrange it."
            : null;
    }
}
