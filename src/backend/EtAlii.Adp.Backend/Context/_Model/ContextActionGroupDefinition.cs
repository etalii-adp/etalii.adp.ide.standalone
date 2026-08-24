namespace EtAlii.Adp.Backend.Context;

/// <summary>One provider's contribution: a set of actions the consumer renders together.</summary>
public sealed record ContextActionGroupDefinition(
    IReadOnlyList<ContextActionDefinition> Actions)
{
    public static ContextActionGroup ToProto(ContextActionGroupDefinition group)
    {
        var result = new ContextActionGroup();
        result.Actions.AddRange(group.Actions.Select(ContextActionDefinition.ToProto));
        return result;
    }


    /// <summary>The project's own actions, on their own message so no selection consumer is disturbed by a history change.</summary>
    public static ContextMessage ToProto(IReadOnlyList<ContextActionGroupDefinition> actions)
    {
        var projectActions = new ContextProjectActions();
        projectActions.Actions.AddRange(actions.Select(ToProto));
        return new ContextMessage { ProjectActions = projectActions };
    }
}
