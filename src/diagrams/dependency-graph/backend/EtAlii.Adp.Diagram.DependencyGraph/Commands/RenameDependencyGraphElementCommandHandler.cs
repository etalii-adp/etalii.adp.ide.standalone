using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a rename. The inverse is the same command carrying the previous label.</summary>
public sealed class RenameDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RenameDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, RenameDependencyGraphElementCommand command)
    {
        var element = DependencyGraphEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That node is no longer in this graph.");
        }

        DependencyGraphWriter.SetLabel(body, element, command.Label);
        return EditOutcome.UndoneBy(command with { Label = element.Label });
    }
}
