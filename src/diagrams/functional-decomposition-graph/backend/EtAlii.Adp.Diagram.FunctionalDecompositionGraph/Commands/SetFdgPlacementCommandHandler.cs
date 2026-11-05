using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Moves an element by setting its top-left; a coordinate that did not change is not written.</summary>
public sealed class SetFdgPlacementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgPlacementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, SetFdgPlacementCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return FdgDefinition.EndOf(model.Diagram, command.ElementId) is { } element
            ? FdgDefinition.Set(body, element, new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = command.X, ["y"] = command.Y })
            : Gone;
    }
}
