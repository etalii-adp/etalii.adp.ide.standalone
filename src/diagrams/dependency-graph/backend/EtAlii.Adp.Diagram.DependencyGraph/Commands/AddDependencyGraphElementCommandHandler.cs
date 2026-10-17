using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out an add, as the binding inserts a node. The undo is the host's restore.</summary>
public sealed class AddDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<AddDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(AddDependencyGraphElementCommand command) =>
        string.IsNullOrWhiteSpace(command.Id)
            ? CommandResult.Failure("A node needs an id.")
            : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, AddDependencyGraphElementCommand command)
    {
        if (DependencyGraphEdits.NodeOf(model, command.Id) is not null)
        {
            // Redo lands here when the node survived - re-adding it would duplicate the id.
            return EditOutcome.Refused("A node with that id is already in this graph.");
        }

        return DependencyGraphDefinition.Apply(body, DependencyGraphEdits.Transaction(
            DependencyGraphEdits.Create(command.Id, command.Label, command.X, command.Row)));
    }
}
