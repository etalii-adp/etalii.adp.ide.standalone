using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Adds an element at a drop: the definition's toolbox tool for its type, dropped there by
/// <see cref="OperationInterpreter.Drop"/>, which names, places and sizes it.
/// </summary>
public sealed class AddFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<AddFdgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddFdgElementCommand command, CancellationToken cancellationToken)
    {
        return FdgDefinition.ToolOf(command.ElementType) is null
            ? CommandResult.Failure($"This notation has no `{command.ElementType}` element.")
            // Minted once and carried as the redo, so a redone element keeps its id.
            : await base.RunAsync(command.ElementId.Length > 0 ? command : command with { ElementId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, AddFdgElementCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (FdgDefinition.IsTaken(model.Diagram, command.ElementId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        // The drop is the centre; the tool's initial values put the top-left where the document holds it.
        var position = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = command.X, ["y"] = command.Y };
        return FdgDefinition.Apply(body, OperationInterpreter.Drop(FdgDefinition.Specification, FdgDefinition.ToolOf(command.ElementType)!, model.Diagram, position, DislIds.Fixed(command.ElementId)));
    }
}
