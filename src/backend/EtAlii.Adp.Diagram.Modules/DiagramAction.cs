using EtAlii.Adp.Context;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// One action of a diagram, written once where a module used to map it in each place it is
/// reached from (code-structure FR-034): its id, what it applies to, how it is offered, and the
/// command it makes. The menu, the commit leg and the property grid ask the same record, so an
/// action cannot mean one thing from the menu and another from the grid.
/// </summary>
/// <remarks>
/// <b>Applying and being offered are separate</b>, as they were in every module that mapped them
/// by hand: "stop evolving" is offered only on something that evolves, but run on something that
/// does not, it still makes its command, and the command's handler says why it refuses.
/// </remarks>
/// <typeparam name="TSubject">What the module knows of the selection the action is run on.</typeparam>
/// <param name="Id">The action's id, as the client sends it.</param>
/// <param name="AppliesTo">Whether the action can run on a subject; one it does not apply to is refused before a command is made.</param>
/// <param name="Command">
/// The command the action makes on a subject, from the value the user gave: empty when it asked for
/// none. Null when the subject no longer holds what the action works on.
/// </param>
public sealed record DiagramAction<TSubject>(
    string Id,
    Func<TSubject, bool> AppliesTo,
    Func<TSubject, string, ICommand?> Command)
{
    /// <summary>How the action is offered on a subject, or null where it is not offered; never, by default.</summary>
    public Func<TSubject, ContextActionDefinition?> Offer { get; init; } = _ => null;

    /// <summary>Which of the menu's groups the action is offered in; groups keep the order of their first action.</summary>
    public string Group { get; init; } = "";

    /// <summary>What the action asks before it runs - a value, a choice, or a refusal - or null when it runs at once.</summary>
    public Func<TSubject, ContextExecutionResult?> Ask { get; init; } = _ => null;

    /// <summary>What the user confirms before the action removes something, or null when it removes nothing more than they see.</summary>
    public Func<TSubject, RemovalConfirmation?> Confirm { get; init; } = _ => null;

    /// <summary>Whether a typed value may be committed; anything, by default.</summary>
    public Func<string, ContextValidationResult> Validate { get; init; } = _ => ContextValidationResult.Accepted;
}

/// <summary>What a module does with its list of <see cref="DiagramAction{TSubject}"/> records.</summary>
public static class DiagramActions
{
    /// <summary>The actions offered on <paramref name="subject"/>, in their groups.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Offered<TSubject>(this IEnumerable<DiagramAction<TSubject>> actions, TSubject subject)
    {
        ArgumentNullException.ThrowIfNull(actions);

        return actions
            .Select(action => (action.Group, Definition: action.Offer(subject)))
            .Where(offered => offered.Definition is not null)
            .GroupBy(offered => offered.Group, StringComparer.Ordinal)
            .Select(group => new ContextActionGroupDefinition(group.Select(offered => offered.Definition!).ToArray()))
            .ToArray();
    }

    /// <summary>The action <paramref name="id"/> names, where it applies to <paramref name="subject"/>; otherwise null.</summary>
    public static DiagramAction<TSubject>? Applying<TSubject>(this IEnumerable<DiagramAction<TSubject>> actions, string id, TSubject subject)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.FirstOrDefault(action => action.Id == id && action.AppliesTo(subject));
    }

    /// <summary>The action <paramref name="id"/> names, whatever it applies to; otherwise null.</summary>
    public static DiagramAction<TSubject>? Named<TSubject>(this IEnumerable<DiagramAction<TSubject>> actions, string id)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.FirstOrDefault(action => action.Id == id);
    }
}
