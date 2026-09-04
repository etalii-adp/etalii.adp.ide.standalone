using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// A directed edge: <see cref="From"/> <b>depends on</b> <see cref="To"/>.
/// </summary>
/// <remarks>
/// The one place this type differs from the timeline by addition rather than deletion. The
/// timeline's connections are deliberately arbitrary - whatever the author means by a line.
/// These mean one thing, the direction is part of the document rather than a drawing convention,
/// and the canvas draws the arrowhead at the <see cref="To"/> end because that is the dependency.
/// </remarks>
/// <param name="Id">The stable identifier, from the document.</param>
/// <param name="From">The dependent element's id - the one that needs the other.</param>
/// <param name="To">The dependency's id - the one that is needed.</param>
/// <param name="Label">What the author calls it, or empty.</param>
/// <param name="Range">The lines that declare it.</param>
public sealed record DependencyGraphRelation(
    string Id,
    string From,
    string To,
    string Label,
    LineRange Range);
