using EtAlii.Adp.Common.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// One action a provider offers for a target. An action that currently cannot be
/// performed is reported with <see cref="Available"/> false and a reason rather than
/// omitted, so the consumer can show it disabled and explain why.
/// </summary>
public sealed record ContextActionDefinition(
    string Id,
    string Label,
    string Icon,
    ContextShortcutDefinition? Shortcut = null,
    bool Available = true,
    string UnavailableReason = "",
    IReadOnlyList<ContextActionGroupDefinition>? Children = null)
{

    public static ContextAction ToProto(ContextActionDefinition action)
    {
        var result = new ContextAction
        {
            Id = action.Id,
            Label = action.Label,
            Icon = action.Icon,
            Available = action.Available,
            UnavailableReason = action.UnavailableReason,
        };

        if (action.Shortcut is { } shortcut)
        {
            result.Shortcut = new ContextShortcut
            {
                Key = shortcut.Key,
                Ctrl = shortcut.Ctrl,
                Shift = shortcut.Shift,
                Alt = shortcut.Alt,
                Meta = shortcut.Meta,
            };
        }

        if (action.Children is { } children)
        {
            result.Items.AddRange(children.Select(ContextActionGroupDefinition.ToProto));
        }

        return result;
    }
}
