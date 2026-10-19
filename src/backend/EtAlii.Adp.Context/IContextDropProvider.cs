namespace EtAlii.Adp.Context;

/// <summary>
/// A provider that resolves a toolbox entry dropped on a target by the entry itself, rather than by
/// looking its drop action up in the target's menu. A module opts in by implementing it beside
/// <see cref="IContextActionProvider"/>; one that does not is asked through its menu, as for any action.
/// </summary>
/// <remarks>
/// The entry says where it may land and what is said when it may not (DISL 7.2 <c>drop.targets</c>,
/// <c>drop.refusal</c>), and an element's menu need not offer what can be dropped on it: a C4 element's
/// menu has no add, yet a container dropped on a software system goes inside it.
/// </remarks>
public interface IContextDropProvider
{
    /// <summary>
    /// The action a drop of the entry whose drop action is <paramref name="dropActionId"/> runs on
    /// <paramref name="target"/>; reported unavailable, with the sentence to show, when it may not land
    /// there; null when this provider does not answer that entry on that target, so the next one is asked.
    /// </summary>
    ValueTask<ContextActionDefinition?> ResolveDropAsync(ContextTarget target, string dropActionId, CancellationToken cancellationToken);
}
