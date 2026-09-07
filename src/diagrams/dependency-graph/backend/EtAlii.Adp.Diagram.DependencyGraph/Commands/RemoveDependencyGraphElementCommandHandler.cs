

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a removal. The inverse is a byte-exact restore of the removed lines, captured
/// before anything is spliced - not a re-creation from the model, which would lose the comments
/// and formatting the author had inside the removed block.
/// </summary>
public sealed class RemoveDependencyGraphElementCommandHandler : ICommandHandler<RemoveDependencyGraphElementCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RemoveDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveDependencyGraphElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be removed until the file is fixed."));
        }

        var element = DependencyGraphEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That node is no longer in this graph."));
        }

        // Captured in ascending order before any splice moves a line, so the restore can replay
        // them ascending against the document as it was.
        var going = DependencyGraphWriter.RelationsTouching(entry.Model, element.Id);
        var segments = going
            .Select(relation => relation.Range)
            .Append(element.Range)
            .OrderBy(range => range.Start)
            .Select(range => DependencyGraphEdits.Capture(entry.Document, range))
            .ToList();

        DependencyGraphWriter.RemoveElement(entry.Document, entry.Model, element);

        if (command.RemoveEmptiedRelationsSection)
        {
            // This removal is the undo of an insert that created the relations: key on demand,
            // so the key it created goes too - or the undo comes back one line different.
            DependencyGraphWriter.RemoveRelationsSectionIfEmpty(
                entry.Document, DependencyGraphParser.Parse(entry.Document));
        }

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RestoreDependencyGraphLinesCommand(command.BodyPath, command.ElementId, segments))
            : CommandResult.Failure(error));
    }
}
