using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class DiagramIdentifier : IEquatable<DiagramIdentifier>
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

    public override string ToString() => $"(Diagram: {Identifier.ToString()})";

    public bool Equals(DiagramIdentifier? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Identifier.Equals(other.Identifier);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((DiagramIdentifier)obj);
    }

    public override int GetHashCode() => Identifier.GetHashCode();
}