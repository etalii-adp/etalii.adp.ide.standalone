using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.Tests.Support;

/// <summary>
/// What a test reads off a discovered menu: its actions, group after group, or just their ids.
/// </summary>
/// <remarks>
/// Fifty-odd tests spelled <c>groups.SelectMany(group => group.Actions).Select(action => action.Id)</c>
/// out in full, against both the provider's groups and the wire's. Only the top level is read, as
/// those spellings did; a submenu's children are its parent action's business.
/// </remarks>
public static class ContextMenus
{
    /// <summary>The top-level actions of every group, in order.</summary>
    public static IReadOnlyList<ContextActionDefinition> Actions(this IEnumerable<ContextActionGroupDefinition> groups) =>
        [.. groups.SelectMany(group => group.Actions)];

    /// <summary>The ids of the top-level actions of every group, in order.</summary>
    public static IReadOnlyList<string> ActionIds(this IEnumerable<ContextActionGroupDefinition> groups) =>
        [.. groups.SelectMany(group => group.Actions).Select(action => action.Id)];

    /// <summary>The top-level actions of every group on the wire, in order.</summary>
    public static IReadOnlyList<ContextAction> Actions(this IEnumerable<ContextActionGroup> groups) =>
        [.. groups.SelectMany(group => group.Actions)];

    /// <summary>The ids of the top-level actions of every group on the wire, in order.</summary>
    public static IReadOnlyList<string> ActionIds(this IEnumerable<ContextActionGroup> groups) =>
        [.. groups.SelectMany(group => group.Actions).Select(action => action.Id)];
}
