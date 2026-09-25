using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// Contributes the actions available for a target within one scope, and performs them.
/// This is the extensibility seam: a later module adds actions - complete with their own
/// keyboard shortcuts - by registering another implementation, without any core code
/// learning that the new actions exist.
/// </summary>
/// <remarks>
/// A provider never resolves a location itself; <see cref="ContextTarget.ResolvedFullPath"/>
/// arrives already resolved and containment-checked by the service layer.
/// </remarks>
public interface IContextActionProvider
{
    /// <summary>The scope this provider contributes to; it is consulted for no other.</summary>
    ContextScope Scope { get; }

    /// <summary>
    /// The actions currently offered for <paramref name="target"/>. An action that cannot
    /// currently be performed is reported unavailable with a reason rather than omitted;
    /// a target that no longer exists yields no groups at all.
    /// </summary>
    ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken);

    /// <summary>Starts an action, saying what (if anything) still has to be asked of the user.</summary>
    ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken);

    /// <summary>Judges a value the user proposed for an in-flight action.</summary>
    ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Performs the action for real, after the user confirmed it. <paramref name="value"/> is
    /// the dialog's primary answer - the typed text, or the chosen option id; <paramref name="text"/>
    /// carries a text field the dialog showed beside that choice, and is empty when it showed none.
    /// </summary>
    ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken);
}
