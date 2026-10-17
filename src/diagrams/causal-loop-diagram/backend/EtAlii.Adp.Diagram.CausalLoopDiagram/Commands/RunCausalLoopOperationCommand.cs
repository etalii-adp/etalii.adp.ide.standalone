using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// Runs one of the definition's operations - <c>addLink</c>, <c>connectLink</c>, <c>claimLoop</c>, <c>toggleDelay</c>,
/// <c>flipCurve</c> - on the element a gesture chose, with the parameters its dialog answered.
/// </summary>
/// <remarks>
/// The operation is run again on every execution, redo included, against the document as it then is; what it
/// writes depends on nothing minted when the gesture happened.
/// </remarks>
/// <param name="BodyPath">The document to write to.</param>
/// <param name="Operation">The operation's name in the definition.</param>
/// <param name="Target">The element it runs on, by its wire id; null for one on the diagram.</param>
/// <param name="Parameters">The answered parameters, by name; a variable is answered with its name.</param>
public sealed record RunCausalLoopOperationCommand(
    string BodyPath,
    string Operation,
    string? Target,
    IReadOnlyDictionary<string, string> Parameters) : IDocumentEditCommand;

/// <inheritdoc cref="RunCausalLoopOperationCommand" />
public sealed class RunCausalLoopOperationCommandHandler(ICausalLoopDocumentStore documents)
    : CausalLoopEditHandler<RunCausalLoopOperationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, RunCausalLoopOperationCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopDefinition.Run(body, command.Operation, command.Target, command.Parameters);
    }
}
