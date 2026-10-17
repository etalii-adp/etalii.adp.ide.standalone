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

        DependencyGraphWriter.RemoveElement(body, model, element);
        return EditOutcome.Applied;
    }
}
