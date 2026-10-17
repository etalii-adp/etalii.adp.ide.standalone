using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a create-and-relate: the node, then the dependency, in one body, which is stored only when
/// both were written. The undo is the host's restore, which takes both back.
/// </summary>
public sealed class AddConnectedDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<AddConnectedDependencyGraphElementCommand>(documents)
{
    /// <summary>What a node created by a gesture is called until somebody renames it.</summary>
    internal const string NewElementLabel = "New node";

    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, AddConnectedDependencyGraphElementCommand command)
    {
        if (DependencyGraphEdits.NodeOf(model, command.FromElementId) is null)
        {
            return EditOutcome.Refused("The node this dependency starts from is no longer in this graph.");
        }

        if (DependencyGraphEdits.NodeOf(model, command.NewElementId) is not null)
        {
            // Redo lands here when the node survived - re-adding would duplicate the id.
            return EditOutcome.Refused("A node with that id is already in this graph.");
        }

        // Which way the dependency runs is the type's whole meaning, so the gesture decides it
        // rather than the writer: dragged out of a node, that node depends on what was made;
        // dragged into one, what was made depends on it.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        return DependencyGraphDefinition.Apply(body, DependencyGraphEdits.Transaction(
            DependencyGraphEdits.Create(command.NewElementId, command.Label ?? NewElementLabel, command.X, command.Row),
            DependencyGraphEdits.Connect(command.RelationId, source, target, "")));
    }
}
