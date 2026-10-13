using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes a trend or trigger with its influences, or a note.</summary>
public sealed class RemoveGhgElementCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<RemoveGhgElementCommand>(documents)
{
    // The definition's deletion policy: the influences at a trend or trigger, then the element.
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, RemoveGhgElementCommand command) =>
        model.IsElement(command.ElementId) && GhgDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { } element
            ? GhgDefinition.Apply(document, DeletionPolicy.Changes(GhgDefinition.Specification, element))
            : GhgRefusals.Gone;
}
