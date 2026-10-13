using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes an element and its connections in one edit.</summary>
public sealed class RemoveFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RemoveFdgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, RemoveFdgElementCommand command) =>
        model.ElementOf(command.ElementId) is { } element
            ? FdgWriter.RemoveElement(document, model, element)
            : Gone;
}
