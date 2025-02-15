using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public class LinkIdentifier : IEquatable<LinkIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator LinkIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(LinkIdentifier did) => did.Identifier;

    public static bool operator ==(LinkIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(LinkIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, LinkIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, LinkIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(LinkIdentifier? left, LinkIdentifier? right) => Equals(left, right);
    public static bool operator !=(LinkIdentifier? left, LinkIdentifier? right) => !Equals(left, right);

    public static LinkIdentifier NewIdentifier() => (LinkIdentifier)Guid.NewGuid();

    public override string ToString() => Identifier.ToString();

    public bool Equals(LinkIdentifier? other)
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
        return Equals((LinkIdentifier)obj);
    }

    public override int GetHashCode() => Identifier.GetHashCode();
}