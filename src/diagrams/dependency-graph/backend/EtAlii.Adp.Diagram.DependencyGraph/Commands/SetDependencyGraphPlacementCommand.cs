using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Puts one node somewhere: a coordinate and a row. The one command behind a drag and a grid
/// edit, whose inverse is the same command carrying the previous two values.
/// </summary>
/// <remarks>
/// The timeline's version of this carried a begin, an end and a row, and the design flagged the
/// single-command choice as the judgement a reviewer might make the other way. With time gone
/// there are two values rather than three and only one gesture that changes them, so the case
/// for splitting it is weaker here than it was there. <paramref name="Description"/> still
/// carries what the gesture was, and travels into the inverse unchanged.
/// </remarks>
/// <param name="BodyPath">The document the node lives in.</param>
/// <param name="ElementId">Which node.</param>
/// <param name="X">The new horizontal coordinate, in canvas units.</param>
/// <param name="Row">The new row.</param>
/// <param name="Description">What the gesture was - "Moved", "Edited" - for the history.</param>
public sealed record SetDependencyGraphPlacementCommand(
    string BodyPath,
    string ElementId,
    double X,
    int Row,
    string Description) : ICommand;
