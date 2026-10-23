using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Resizes an element, raised to its minimums (<see cref="FdgSizeClamp"/>); a height only for a Comment.</summary>
public sealed class SetFdgSizeCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgSizeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, SetFdgSizeCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (FdgDefinition.EndOf(model.Diagram, command.ElementId) is not { } element)
        {
            return Gone;
        }

        if (command.Height is not null && !FdgDefinition.IsComment(element))
        {
            return EditOutcome.Refused("Only a Comment has its own height; the others share one.");
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["width"] = FdgSizeClamp.Of(element, "width", command.Width) };
        if (command.Height is { } height) values["height"] = FdgSizeClamp.Of(element, "height", height);
        return FdgDefinition.Set(body, element, values);
    }
}
