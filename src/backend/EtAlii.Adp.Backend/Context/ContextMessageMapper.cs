namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// The one place backend context records become wire messages, shared by the service
/// and the selection store so a pushed selection and a discovered action list always
/// have the same shape.
/// </summary>
public static class ContextMessageMapper
{
    /// <param name="rootActions">
    /// What applies when nothing is selected - the project root's actions. Carried on the
    /// "nothing selected" message so the explorer's empty space has a menu without a round
    /// trip; the selection itself stays absent, because nothing is selected.
    /// </param>
    public static ContextMessage ToMessage(
        ContextSelectionRecord? record,
        IReadOnlyList<ContextActionGroupDefinition>? rootActions = null,
        bool transient = false)
    {
        var changed = new ContextSelectionChanged { Transient = transient };
        if (record is not null)
        {
            changed.Selection = record.Chain;
            changed.Levels.AddRange(record.Levels.Select(level => level.Detail));
            changed.Actions.AddRange(record.Actions.Select(ToProto));
        }
        else if (rootActions is not null)
        {
            changed.Actions.AddRange(rootActions.Select(ToProto));
        }

        return new ContextMessage { Selection = changed };
    }

    public static ContextActionGroup ToProto(ContextActionGroupDefinition group)
    {
        var result = new ContextActionGroup();
        result.Actions.AddRange(group.Actions.Select(ToProto));
        return result;
    }

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
            result.Items.AddRange(children.Select(ToProto));
        }

        return result;
    }
}
