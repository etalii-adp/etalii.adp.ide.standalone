using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a placement: locate against current state, refuse with a sentence when that fails,
/// and set the node's <c>x</c> and <c>row</c> as the binding writes them. The undo is the host's restore.
/// </summary>
/// <remarks>
/// The timeline's handler also enforced end-before-begin here, because a guard only in the client
/// is a guard a second client walks around. That guard went with the dates; a coordinate has no
/// invalid value to refuse, so this handler's only precondition is that the node still exists.
/// It is still checked against the document as it is now rather than trusted from when the
/// command was made, because undo and redo dispatch the same instance again later.
/// </remarks>
public sealed class SetDependencyGraphPlacementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<SetDependencyGraphPlacementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, SetDependencyGraphPlacementCommand command) =>
        DependencyGraphEdits.Set(
            body,
            DependencyGraphEdits.Node,
            command.ElementId,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = command.X, ["row"] = (long)command.Row },
            "That node is no longer in this graph.");
}
