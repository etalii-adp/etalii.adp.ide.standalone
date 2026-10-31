using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The rows of the diagram itself (etalii.adp spec 074, User Story 8): what a causal loop diagram's file shows with nothing
/// on its canvas selected. Which rows, their values and what an edit writes are the definition's form for the diagram.
/// </summary>
public sealed class CausalLoopDiagramPropertyProvider(
    IHistoryStackStore historyStacks, ICausalLoopDocumentStore documents, Func<DiagramFileRouter> router) : IContextPropertyProvider
{
    /// <inheritdoc />
    public ContextScope Scope => ContextScope.Hierarchy;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BodyOf(target) is { } body
            ? CausalLoopDefinition.DiagramRows(await documents.GetOrLoadAsync(body, cancellationToken))
            : [];
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(ContextTarget target, string propertyId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (BodyOf(target) is not { } path) return ContextPropertyResult.Failure("That file is not a causal loop diagram.");

        var entry = await documents.GetOrLoadAsync(path, cancellationToken);
        if (!entry.IsUsable) return ContextPropertyResult.Failure(CausalLoopEdits.Unreadable);

        // A value set to what the row shows is no change, so nothing lands on the undo stack.
        if (CausalLoopDefinition.DiagramRows(entry).Any(row => row.Id == propertyId && row.Value == value)) return ContextPropertyResult.Success;

        var committed = CausalLoopDefinition.DiagramRow(entry.Document, propertyId, value);
        if (committed.Refusal is { } refusal) return ContextPropertyResult.Failure(refusal);

        var result = await historyStacks.Get(target.Root).ExecuteAsync(new SetDiagramAttributeCommand(path, committed.Assigned), cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    /// <summary>The body of the causal loop diagram <paramref name="target"/> is the file or registration of; null for anything else.</summary>
    private string? BodyOf(ContextTarget target) =>
        !target.IsContainer && router().Route(target.ResolvedFullPath, target.Root.FullPath) is DiagramRouted { BodyPath: { } body } routed
        && routed.Definition.Origin == Diagram.CausalLoop.Origin
            ? body
            : null;
}
