using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class DiagramIdentifier
{
    public required Guid Identifier { get; init; }
    
    public static implicit operator DiagramIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(DiagramIdentifier did) => did.Identifier;
    
    public override string ToString() => $"(Diagram: {Identifier.ToString()})";
}