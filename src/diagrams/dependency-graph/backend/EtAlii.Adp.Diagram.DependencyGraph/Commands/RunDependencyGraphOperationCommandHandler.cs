using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out an operation's transaction, change by change, each as the binding plans it; the body is stored
/// only when every change was written. The undo is the host's restore.
/// </summary>
public sealed class RunDependencyGraphOperationCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RunDependencyGraphOperationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, RunDependencyGraphOperationCommand command)
    {
        // Redo lands here when what the operation made survived - writing it again would duplicate its ids.
        foreach (var change in command.Transaction.Changes)
        {
            if (change is DislChange.Create create && DependencyGraphEdits.NodeOf(model, create.Id) is not null)
            {
                return EditOutcome.Refused("A node with that id is already in this graph.");
            }
        }

        return DependencyGraphDefinition.Apply(body, command.Transaction);
    }
}
