using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets the attributes of the diagram itself a row of its form assigns, each in its stored form, or forgets those that are null, as one edit.</summary>
public sealed record SetDiagramAttributeCommand(string BodyPath, IReadOnlyList<(string Name, object? Value)> Assigned) : IDocumentEditCommand;

/// <inheritdoc cref="SetDiagramAttributeCommand" />
public sealed class SetDiagramAttributeCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetDiagramAttributeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetDiagramAttributeCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(command);

        foreach ((string name, object? value) in command.Assigned)
        {
            var edit = body.SetDiagramAttribute(name, value);
            if (!edit.WasApplied) return edit;
        }
        return EditOutcome.Applied;
    }
}
