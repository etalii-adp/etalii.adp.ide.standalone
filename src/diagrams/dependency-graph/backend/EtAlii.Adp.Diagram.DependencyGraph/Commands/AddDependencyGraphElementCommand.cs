

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Adds one node at an authored position.
/// </summary>
/// <remarks>
/// The id is the caller's to supply, generated once where the gesture happens - so a redo
/// re-creates the node under the id it had, and every reference made in between stays good.
/// </remarks>
/// <param name="BodyPath">The document to add to.</param>
/// <param name="Id">The new node's id.</param>
/// <param name="Label">What it is called.</param>
/// <param name="X">Its horizontal coordinate, in canvas units.</param>
/// <param name="Row">The row it lands on.</param>
public sealed record AddDependencyGraphElementCommand(
    string BodyPath,
    string Id,
    string Label,
    double X,
    int Row) : ICommand;
