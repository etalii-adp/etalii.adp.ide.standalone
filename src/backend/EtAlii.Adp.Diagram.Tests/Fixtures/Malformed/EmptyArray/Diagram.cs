namespace EtAlii.Adp.Diagram.Tests.Fixtures.Malformed.EmptyArray;

/// <summary>
/// Declares the property and then nothing in it. Legal C#, and silent under the singular
/// shape only because it could not be expressed; worth a warning rather than a shrug, since
/// a module that ships no types is almost certainly a mistake rather than a choice.
/// </summary>
public static class Diagram
{
    public static DiagramDefinition[] Definitions { get; } = [];
}
