using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a create-and-relate. The inverse is the removal of the new node, whose relations -
/// including the one made here - go with it.
/// </summary>
public sealed class AddConnectedDependencyGraphElementCommandHandler
    : ICommandHandler<AddConnectedDependencyGraphElementCommand>
{
    /// <summary>What a node created by a gesture is called until somebody renames it.</summary>
    internal const string NewElementLabel = "New node";

    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public AddConnectedDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(
        AddConnectedDependencyGraphElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be added until the file is fixed."));
        }

        if (DependencyGraphEdits.ElementOf(entry.Model, command.FromElementId) is null)
        {
            return Task.FromResult(CommandResult.Failure(
                "The node this dependency starts from is no longer in this graph."));
        }

        if (DependencyGraphEdits.ElementOf(entry.Model, command.NewElementId) is not null)
        {
            // Redo lands here when the node survived - re-adding would duplicate the id.
            return Task.FromResult(CommandResult.Failure("A node with that id is already in this graph."));
        }

        DependencyGraphWriter.InsertElement(
            entry.Document, entry.Model, command.NewElementId, command.Label ?? NewElementLabel, command.X, command.Row);

        // The insert moved lines, so the relation is spliced against a fresh parse rather than
        // the ranges the first model recorded.
        var reparsed = DependencyGraphParser.Parse(entry.Document);
        // Whether the insert is about to create the relations: key, so the inverse can take
        // the key back out and the undo return the file byte for byte.
        var hadSection = DependencyGraphWriter.HasRelationsSection(entry.Document);

        // Which way the dependency runs is the type's whole meaning, so the gesture decides it
        // rather than the writer: dragged out of a node, that node depends on what was made;
        // dragged into one, what was made depends on it.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        DependencyGraphWriter.InsertRelation(
            entry.Document, reparsed, command.RelationId, source, target, "");

        var saved = _documents.Save(command.BodyPath, entry);
        return Task.FromResult(!saved.Failed
            ? CommandResult.Success(new RemoveDependencyGraphElementCommand(
                command.BodyPath, command.NewElementId, RemoveEmptiedRelationsSection: !hadSection))
            : CommandResult.Failure(saved.Error));
    }
}
