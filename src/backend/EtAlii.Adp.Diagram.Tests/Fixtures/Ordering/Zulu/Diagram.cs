using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Zulu;

/// <summary>Vendor sorts last, type sorts first - the pair proves vendor wins.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } = [new(new ToolOrigin("zulu", "a"), "Zulu A")];
}
