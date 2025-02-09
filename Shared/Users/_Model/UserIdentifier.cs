using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class UserIdentifier : IEquatable<UserIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator UserIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(UserIdentifier uid) => uid.Identifier;

    public static bool operator ==(UserIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(UserIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, UserIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, UserIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(UserIdentifier? left, UserIdentifier? right) => Equals(left, right);
    public static bool operator !=(UserIdentifier? left, UserIdentifier? right) => !Equals(left, right);

    public override string ToString() => $"(Diagram: {Identifier.ToString()})";

    public bool Equals(UserIdentifier? other)
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
        return Equals((UserIdentifier)obj);
    }

    public override int GetHashCode() => Identifier.GetHashCode();
}