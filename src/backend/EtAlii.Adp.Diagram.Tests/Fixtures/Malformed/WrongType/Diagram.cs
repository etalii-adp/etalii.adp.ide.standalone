namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.WrongType;

/// <summary>Right name, right shape, wrong property type - must be skipped and reported.</summary>
public static class Diagram
{
    public static string Definition { get; } = "not a DiagramDefinition";
}
