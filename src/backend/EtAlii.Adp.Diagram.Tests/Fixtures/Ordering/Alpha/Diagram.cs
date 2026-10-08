using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Ordering.Alpha;

/// <summary>Vendor sorts first, type sorts last - ordering is by vendor before type.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } = [new(new DiagramOrigin("alpha", "z"), "Alpha Z")];
}
