using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

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


}
