using EtAlii.Adp.Context.Wire;

namespace EtAlii.Adp.Context;

/// <summary>
/// Maps the shared action-group contract onto the context stream's wire envelope. Lives at
/// the wire boundary rather than on the descending record, because ContextMessage is the
/// service's own shape and stays with context.proto while the record lives in Common
/// (backend-project-decomposition task 10).
/// </summary>
internal static class ContextActionGroups
{
    /// <summary>The project's own actions, on their own message so no selection consumer is disturbed by a history change.</summary>
    public static ContextMessage ToProto(IReadOnlyList<ContextActionGroupDefinition> actions)
    {
        var projectActions = new ContextProjectActions();
        projectActions.Actions.AddRange(actions.Select(ContextActionGroupDefinition.ToProto));
        return new ContextMessage { ProjectActions = projectActions };
    }
}
