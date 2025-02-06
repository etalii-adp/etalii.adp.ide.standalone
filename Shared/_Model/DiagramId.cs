using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public struct DiagramIdentifier
{
    public Guid Identifier { get; set; }

    private DiagramIdentifier(Guid identifier)
    {
        Identifier = identifier;
    }
    
    public static implicit operator DiagramIdentifier(Guid id) => new(id);
    public static explicit operator Guid(DiagramIdentifier did) => did.Identifier;
    
    public override string ToString() => $"(Diagram: {Identifier.ToString()})";
}