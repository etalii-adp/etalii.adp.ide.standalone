using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Resizes an element, clamped to its minimums.</summary>
public sealed class SetFdgSizeCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgSizeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, SetFdgSizeCommand command) =>
        model.ElementOf(command.ElementId) is { } element
            ? FdgWriter.Resize(document, element, command.Width, command.Height)
            : Gone;
}
