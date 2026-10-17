using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a create-and-relate. The inverse is the removal of the new node, whose relations -
/// including the one made here - go with it.
/// </summary>
public sealed class AddConnectedDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<AddConnectedDependencyGraphElementCommand>(documents)
{
    /// <summary>What a node created by a gesture is called until somebody renames it.</summary>
    internal const string NewElementLabel = "New node";

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, AddConnectedDependencyGraphElementCommand command)
    {
        if (DependencyGraphEdits.ElementOf(model, command.FromElementId) is null)
        {
            return EditOutcome.Refused("The node this dependency starts from is no longer in this graph.");
        }

        if (DependencyGraphEdits.ElementOf(model, command.NewElementId) is not null)
        {
            // Redo lands here when the node survived - re-adding would duplicate the id.
            return EditOutcome.Refused("A node with that id is already in this graph.");
        }

        DependencyGraphWriter.InsertElement(
            body, model, command.NewElementId, command.Label ?? NewElementLabel, command.X, command.Row);

        // The insert moved lines, so the relation is spliced against a fresh parse rather than
        // the ranges the first model recorded.
        var reparsed = DependencyGraphParser.Parse(body);
        // Which way the dependency runs is the type's whole meaning, so the gesture decides it
        // rather than the writer: dragged out of a node, that node depends on what was made;
        // dragged into one, what was made depends on it.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        DependencyGraphWriter.InsertRelation(body, reparsed, command.RelationId, source, target, "");

        return EditOutcome.Applied;
    }
}
