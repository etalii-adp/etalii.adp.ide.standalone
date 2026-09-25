using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.NotStatic;

/// <summary>
/// A non-static class named Diagram. Not a candidate at all (a static class is what compiles to
/// abstract + sealed), so it must be passed over silently rather than reported as malformed.
/// </summary>
public sealed class Diagram
{
    public DiagramDefinition[] Definitions { get; } = [new(new DiagramOrigin("fixture", "not-static"), "Not Static")];
}
