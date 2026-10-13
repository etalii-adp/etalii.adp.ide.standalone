using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a placement: locate against current state, refuse with a sentence when that
/// fails, splice through <see cref="DependencyGraphWriter"/>, report the inverse.
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
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, SetDependencyGraphPlacementCommand command)
    {
        var element = DependencyGraphEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That node is no longer in this graph.");
        }

        // Each edit is a one-for-one line replace within the node's range, so the ranges the
        // parser recorded stay valid between them and no re-parse is needed mid-command.
        DependencyGraphWriter.SetX(body, element, command.X);
        DependencyGraphWriter.SetRow(body, element, command.Row);

        return EditOutcome.UndoneBy(command with
        {
            X = element.X,
            Row = element.Row,
        });
    }
}
