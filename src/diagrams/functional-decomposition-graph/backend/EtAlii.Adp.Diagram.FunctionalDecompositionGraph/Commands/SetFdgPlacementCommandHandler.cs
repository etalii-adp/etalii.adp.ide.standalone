using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Moves an element by rewriting its top-left.</summary>
public sealed class SetFdgPlacementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgPlacementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, SetFdgPlacementCommand command) =>
        model.ElementOf(command.ElementId) is { } element
            ? FdgWriter.MoveTo(document, element, command.X, command.Y)
            : Gone;
}
