using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Creates one node and the dependency that reaches it, as one command - what a relation dragged
/// onto empty space means: "this depends on something that does not exist yet".
/// </summary>
/// <remarks>
/// One command rather than an add followed by a connect, so the gesture is one history entry
/// and one undo - the inverse removes the new node, which takes its relation with it.
/// Both ids are the caller's to supply, so a redo re-creates both under the ids they had.
/// </remarks>
/// <param name="BodyPath">The document.</param>
/// <param name="FromElementId">The node the relation is dragged from.</param>
/// <param name="NewElementId">The id the new node is born under.</param>
/// <param name="RelationId">The id the relation is born under.</param>
/// <param name="X">Where the gesture landed, in canvas units.</param>
/// <param name="Row">The row the gesture landed on.</param>
/// <param name="NewElementIsSource">
/// Whether the relation runs from the new node into the existing one - a gesture dragged from
/// the existing node's incoming anchor, where the thing that depends on it is the new one -
/// rather than the other way round.
/// </param>
public sealed record AddConnectedDependencyGraphElementCommand(
    string BodyPath,
    string FromElementId,
    string NewElementId,
    string RelationId,
    double X,
    int Row,
    bool NewElementIsSource = false) : ICommand;
