using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The shapes reading: the projection and the grid layout composed over the family store, with the
/// authored <c>layout:</c> overlay on top (shacl-diagram Requirements 2, 3.2).
/// </summary>
/// <remarks>
/// This reading consumes no registration header. The anchor's item-10 facility exists and the
/// family helper is there, but approved Requirement 4.5 declines it: a shapes graph constrains
/// any number of data graphs, so binding a registration to one would misstate the medium, and
/// annotating targets against a chosen data graph is the first step of the execution path that
/// Requirement 4.4 assigns whole to a future spec. There is therefore no header parsing here to
/// find, and its absence is deliberate rather than unfinished.
/// </remarks>
public sealed class ShaclReading(ShaclElementMapper mapper) : IRdfReading<ShaclProjectionResult>
{
    /// <summary>
    /// The cell a card occupies for the purpose of deciding whether it is on screen - the
    /// layout's column pitch, with a height generous enough for a card of many constraints.
    /// Over-inclusion is the safe direction: one extra element on the wire against a hole the
    /// reader looks straight at.
    /// </summary>
    private const double CardWidth = 320;

    /// <inheritdoc cref="CardWidth" />
    private const double CardHeight = 400;

    /// <inheritdoc />
    /// <remarks>What looks like nesting here is a reference - <c>sh:node</c>, or a combinator operand - and those are stated as triples.</remarks>
    public string ParentRefusal => "A shape is not held inside another shape; a reference between them is stated as a triple. Dragging changes where a card sits on the canvas.";

    /// <inheritdoc />
    public ShaclProjectionResult Project(RdfModel model) => ShaclProjection.Project(model, int.MaxValue);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, RegistrationPosition> Positions(
        ShaclProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> stored) =>
        RegistrationLayout.Apply(ShaclLayout.Positions(projection), stored);

    /// <inheritdoc />
    /// <remarks>
    /// The counts handed on are the document's, not this view's. <see cref="ShaclProjectionResult.Truncated" />
    /// drives both the banner and the read-only refusal, and those are decided from the document
    /// with no viewport to consult - so a banner raised by a narrow view would contradict them.
    /// </remarks>
    public ShaclProjectionResult InView(
        ShaclProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var visible = projection.Cards
            .Where(card => RdfViewport.Admits(viewport, positions, card.Id, CardWidth, CardHeight))
            .ToList();

        var truncated = projection.Total > ShaclProjection.DefaultBudget;
        var kept = visible.Count > ShaclProjection.DefaultBudget
            ? visible.Take(ShaclProjection.DefaultBudget).ToList()
            : visible;

        var keptIds = kept.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new ShaclProjectionResult(
            kept,
            edges,
            truncated ? ShaclProjection.DefaultBudget : kept.Count,
            truncated ? projection.Total : kept.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramElement> Elements(
        ShaclProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions) =>
        mapper.Elements(projection, positions);

    /// <inheritdoc />
    public string? Refusal(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId.StartsWith("shacl-edge:", StringComparison.Ordinal) || elementId == RdfElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        // The identity boundary, at the position seam: an anonymous shape draws, but its ordinal
        // belongs to this parse alone, so a stored position could not be trusted.
        return elementId.StartsWith("blank:", StringComparison.Ordinal)
            ? ShaclRefusals.BlankRooted
            : null;
    }
}
