using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology reading: the <c>w3c/owl</c> view of a file over the family's one store, so an edit
/// through any reading of the file is visible here and vice versa (owl-diagram Requirement 8.4).
/// </summary>
/// <remarks>
/// The move refusals extend the family's with the expression case: an <c>expr:</c> id (and a
/// materialized anchor's <c>thing:</c>/<c>dt:</c> id) is deterministic within a parse and
/// deliberately unstable across edits, so a stored position would silently jump to another
/// structure on the next reparse - the blank-node identity boundary (Requirement 3.2).
/// </remarks>
public sealed class OwlReading(OwlElementMapper mapper) : IRdfReading<OwlGraphResult>
{
    /// <inheritdoc />
    public string ParentRefusal => "An ontology element has no parent to move it under; dragging changes where it sits on the canvas.";

    /// <inheritdoc />
    public OwlGraphResult Project(RdfModel model) => OwlProjection.Project(model, int.MaxValue);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, RegistrationPosition> Positions(
        OwlGraphResult projection, IReadOnlyDictionary<string, RegistrationPosition> stored) =>
        OwlLayout.Apply(OwlLayout.Positions(projection), stored);

    /// <inheritdoc />
    /// <remarks>
    /// This is the reading the whole specification was argued from. An ontology of more than
    /// 1,600 resources opened behind a first-N banner that discarded by document order, so
    /// panning could never recover the rest - a strictly worse answer to the same problem than
    /// asking what the reader is looking at. The counts handed on are still the document's,
    /// because <see cref="OwlGraphResult.Truncated" /> drives the banner and the read-only
    /// refusal and both are decided from the document with no viewport to consult.
    /// </remarks>
    public OwlGraphResult InView(
        OwlGraphResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(projection);

        // Each node is judged on the box it actually occupies, not on a nominal cell. This
        // reading draws small shapes beside class cards whose height grows with their rows, and
        // the ontology header - which genuinely sits at the origin - is the tallest of all when
        // it carries a dozen annotations. A fixed cell would cull it while the reader was still
        // looking at its lower half.
        var visible = projection.Nodes
            .Where(node =>
            {
                (double width, double height) = OwlLayout.SizeOf(node);
                return RdfViewport.Admits(viewport, positions, node.Id, width, height);
            })
            .ToList();

        var truncated = projection.Total > RdfProjection.DefaultBudget;
        var kept = visible.Count > RdfProjection.DefaultBudget
            ? visible.Take(RdfProjection.DefaultBudget).ToList()
            : visible;

        var keptIds = kept.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new OwlGraphResult(
            kept,
            edges,
            truncated ? RdfProjection.DefaultBudget : kept.Count,
            truncated ? projection.Total : kept.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramElement> Elements(
        OwlGraphResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions) =>
        mapper.Elements(projection, positions);

    /// <inheritdoc />
    public string? Refusal(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == OwlElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        if (elementId.StartsWith("expr:", StringComparison.Ordinal))
        {
            return "That is an anonymous class expression, whose identity does not survive an edit to the file, so a stored position could not be trusted. It takes its place beside the class that uses it; edit the expression as text.";
        }

        return OwlLayout.IsPositionable(elementId)
            ? null
            : "That element is drawn where its property needs it and cannot be arranged on its own.";
    }
}
