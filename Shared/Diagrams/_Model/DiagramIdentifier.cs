using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public readonly struct DiagramIdentifier : IEquatable<DiagramIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator DiagramIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(DiagramIdentifier did) => did.Identifier;

    public static bool operator ==(DiagramIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(DiagramIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, DiagramIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, DiagramIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(DiagramIdentifier? left, DiagramIdentifier? right) => Equals(left, right);
    public static bool operator !=(DiagramIdentifier? left, DiagramIdentifier? right) => !Equals(left, right);

    public static DiagramIdentifier NewIdentifier() => (DiagramIdentifier)Guid.NewGuid();

    public override string ToString() => Identifier.ToString();
    
    public override bool Equals(object? obj) => obj is DiagramIdentifier other && Equals(other);

    public bool Equals(DiagramIdentifier other) => Identifier.Equals(other.Identifier);

    public override int GetHashCode() => Identifier.GetHashCode();
}