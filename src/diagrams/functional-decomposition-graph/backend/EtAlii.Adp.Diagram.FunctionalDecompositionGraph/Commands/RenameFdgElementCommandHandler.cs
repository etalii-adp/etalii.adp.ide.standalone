using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a Name, or a Comment's text: whichever the element says.</summary>
public sealed class RenameFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RenameFdgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, RenameFdgElementCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return FdgDefinition.EndOf(model.Diagram, command.ElementId) is { } element
            ? FdgDefinition.Set(body, element, new Dictionary<string, object?>(StringComparer.Ordinal) { [FdgDefinition.IsComment(element) ? "text" : "name"] = command.Value })
            : Gone;
    }
}
