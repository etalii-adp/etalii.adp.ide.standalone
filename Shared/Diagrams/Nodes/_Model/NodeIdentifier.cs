using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public class NodeIdentifier : IEquatable<NodeIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator NodeIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(NodeIdentifier did) => did.Identifier;

    public static bool operator ==(NodeIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(NodeIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, NodeIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, NodeIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(NodeIdentifier? left, NodeIdentifier? right) => Equals(left, right);
    public static bool operator !=(NodeIdentifier? left, NodeIdentifier? right) => !Equals(left, right);

    public static NodeIdentifier NewIdentifier() => (NodeIdentifier)Guid.NewGuid();

    public override string ToString() => Identifier.ToString();

    public bool Equals(NodeIdentifier? other)
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
        return Equals((NodeIdentifier)obj);
    }

    public override int GetHashCode() => Identifier.GetHashCode();
}