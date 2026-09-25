using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// What can be done to a selected query element: nothing that changes it.
/// </summary>
/// <remarks>
/// This provider exists to offer no mutating action - and existing is the point. A module that
/// registered nothing at all would leave the question open; one that answers with an empty list
/// has said, in code, that a query diagram is a reading surface. The registration no-writer test
/// asserts exactly this, and the client can therefore never render a gesture the backend would
/// have to refuse (Requirement 6.1).
/// </remarks>
public sealed class SparqlContextActionProvider : IContextActionProvider
{
    /// <summary>What an action arriving anyway is told.</summary>
    public const string NoActionsReason =
        "A query diagram offers no edits: it reads the .rq file, which is edited in a text editor.";

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
    }

    /// <inheritdoc />
    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _ = actionId;

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(NoActionsReason));
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _ = actionId;
        _ = value;

        return ValueTask.FromResult(new ContextValidationResult(false, NoActionsReason));
    }

    /// <inheritdoc />
    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _ = actionId;
        _ = value;
        _ = text;

        return ValueTask.FromResult(ContextCommitResult.Failed(NoActionsReason));
    }
}
