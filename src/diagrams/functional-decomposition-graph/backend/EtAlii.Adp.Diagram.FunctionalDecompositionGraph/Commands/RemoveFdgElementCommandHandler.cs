using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Removes an element and its connections in one edit: the definition's deletion policy, the connections
/// to and from it and then the element.
/// </summary>
public sealed class RemoveFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RemoveFdgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, RemoveFdgElementCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return FdgDefinition.EndOf(model.Diagram, command.ElementId) is { } element
            ? FdgDefinition.Apply(body, DeletionPolicy.Changes(FdgDefinition.Specification, element))
            : Gone;
    }
}
