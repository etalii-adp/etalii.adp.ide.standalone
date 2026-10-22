using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Links two elements - after checking the link the way the canvas does, because the request may not
/// have come from a canvas that checked.
/// </summary>
public sealed class ConnectFdgElementsCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<ConnectFdgElementsCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(ConnectFdgElementsCommand command, CancellationToken cancellationToken) =>
        await base.RunAsync(command.ConnectionId.Length > 0 ? command : command with { ConnectionId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);

    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, ConnectFdgElementsCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        var diagram = model.Diagram;
        if (RefusalOf(diagram, command.RelationType, command.FromId, command.ToId) is { } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        if (FdgDefinition.IsTaken(diagram, command.ConnectionId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        var from = FdgDefinition.EndOf(diagram, command.FromId)!;
        var to = FdgDefinition.EndOf(diagram, command.ToId)!;
        var connect = new DislChange.Connect(FdgReading.RelationTypeOf(command.RelationType)!, command.ConnectionId, from.Id, to.Id, new Dictionary<string, object?>(StringComparer.Ordinal));
        return FdgDefinition.Apply(body, new DislTransaction([connect], [], null));
    }

    /// <summary>
    /// Why a <paramref name="relationType"/> connection from <paramref name="fromId"/> to <paramref name="toId"/>
    /// may not be made in <paramref name="diagram"/> as it is now, never as the client believed; null when it may.
    /// </summary>
    /// <remarks>
    /// The two refusals that are not checks are the tool's own: a relation the notation does not have, and an
    /// end that is no longer an element. The type, cardinality and cycle checks are the definition's, in that
    /// order (<see cref="FdgDefinition.ConnectRefusal"/>).
    /// </remarks>
    internal static string? RefusalOf(DislDiagram diagram, string relationType, string fromId, string toId)
    {
        if (FdgReading.RelationTypeOf(relationType) is not { } type)
        {
            return $"This notation has no `{relationType}` relation.";
        }

        return FdgDefinition.EndOf(diagram, fromId) is not { } from || FdgDefinition.EndOf(diagram, toId) is not { } to
            ? "That link names an element that is no longer in this graph."
            : FdgDefinition.ConnectRefusal(diagram, type, from, to);
    }
}
