using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes a trend or trigger with its influences, or a note.</summary>
public sealed class RemoveGhgElementCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RemoveGhgElementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveGhgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        // The definition's deletion policy: the influences at a trend or trigger, then the element.
        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.IsElement(model, command.ElementId) && GhgDefinition.ElementOf(document.Disl.Diagram, command.ElementId) is { } element
                ? GhgDefinition.Apply(document, DeletionPolicy.Changes(GhgDefinition.Specification, element))
                : GhgEdits.Gone());
    }
}
