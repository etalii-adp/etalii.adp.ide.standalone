using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Whether a positioned node is on screen, for the family's four readings.
/// </summary>
/// <remarks>
/// <para>
/// One test rather than four copies of the same rectangle arithmetic. This is factoring inside
/// one module, not the cross-module backend helper view-delta-adoption Requirement 4 guards
/// against: the four readings genuinely want the same behaviour here, they live in the same
/// assembly, and nothing outside the family can reach it.
/// </para>
/// <para>
/// The cell is the pitch the reading's own layout reserves, rounded up where a node can outgrow
/// it. Over-inclusion is the safe direction and is chosen deliberately: admitting a node just
/// off screen costs one element on the wire, while dropping one that is on screen is a hole the
/// reader looks straight at.
/// </para>
/// </remarks>
internal static class RdfViewport
{
    /// <summary>Whether the cell at <paramref name="position" /> overlaps <paramref name="viewport" />.</summary>
    public static bool Admits(DiagramViewport viewport, RegistrationPosition position, double width, double height) =>
        position.X + width >= viewport.MinX
        && position.X <= viewport.MaxX
        && position.Y + height >= viewport.MinY
        && position.Y <= viewport.MaxY;

    /// <summary>
    /// Whether the node with <paramref name="id" /> is on screen. A node nothing placed is
    /// admitted: no position means no evidence it is off screen, and the alternative is a node
    /// that no viewport ever shows.
    /// </summary>
    public static bool Admits(
        DiagramViewport viewport,
        IReadOnlyDictionary<string, RegistrationPosition> positions,
        string id,
        double width,
        double height) =>
        !positions.TryGetValue(id, out var position) || Admits(viewport, position, width, height);
}
