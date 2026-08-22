namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.Throws;

/// <summary>A getter that throws - must be skipped with the cause in the warning, never propagated.</summary>
public static class Diagram
{
    public static DiagramDefinition Definition => throw new InvalidOperationException("fixture getter failure");
}
