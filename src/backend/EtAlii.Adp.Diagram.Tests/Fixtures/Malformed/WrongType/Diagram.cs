using JetBrains.Annotations;

namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.WrongType;

/// <summary>Right name, right shape, wrong property type - must be skipped and reported.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static string Definitions { get; } = "not a sequence of DiagramDefinition";
}
