using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets the Description of an element or a connection; a blank one removes the key.</summary>
public sealed class SetFdgDescriptionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgDescriptionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, SetFdgDescriptionCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        // A blank Description is none, which the binding writes by removing the key.
        var description = string.IsNullOrWhiteSpace(command.Description) ? "" : command.Description;
        return FdgDefinition.ElementOf(model.Diagram, command.Id) is { } element
            ? FdgDefinition.Set(body, element, new Dictionary<string, object?>(StringComparer.Ordinal) { ["description"] = description })
            : EditOutcome.Refused("That is no longer in this graph.");
    }
}
