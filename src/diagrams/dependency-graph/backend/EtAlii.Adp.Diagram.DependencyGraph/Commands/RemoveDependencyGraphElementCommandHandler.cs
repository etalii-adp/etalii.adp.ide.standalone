using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a removal. The inverse is a byte-exact restore of the removed lines, captured
/// before anything is spliced - not a re-creation from the model, which would lose the comments
/// and formatting the author had inside the removed block.
/// </summary>
public sealed class RemoveDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RemoveDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, RemoveDependencyGraphElementCommand command)
    {
        var element = DependencyGraphEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That node is no longer in this graph.");
        }

        // Captured in ascending order before any splice moves a line, so the restore can replay
        // them ascending against the document as it was.
        var segments = DependencyGraphWriter.RelationsTouching(model, element.Id)
            .Select(relation => relation.Range)
            .Append(element.Range)
            .OrderBy(range => range.Start)
            .Select(range => DependencyGraphEdits.Capture(body, range))
            .ToList();

        DependencyGraphWriter.RemoveElement(body, model, element);

        if (command.RemoveEmptiedRelationsSection)
        {
            // This removal is the undo of an insert that created the relations: key on demand,
            // so the key it created goes too - or the undo comes back one line different.
            DependencyGraphWriter.RemoveRelationsSectionIfEmpty(body, DependencyGraphParser.Parse(body));
        }

        // Redoing the restore removes the node again - by id, which it has again by then.
        return EditOutcome.UndoneBy(new RestoreLinesCommand<IDependencyGraphDocumentStore>(
            command.BodyPath, segments, new RemoveDependencyGraphElementCommand(command.BodyPath, command.ElementId)));
    }
}
