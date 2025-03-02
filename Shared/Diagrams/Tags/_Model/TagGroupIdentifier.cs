using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("TagGroupIdentifier@{ToString()}")]
public readonly struct TagGroupIdentifier : IEquatable<TagGroupIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator TagGroupIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(TagGroupIdentifier did) => did.Identifier;

    public static bool operator ==(TagGroupIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(TagGroupIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, TagGroupIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, TagGroupIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(TagGroupIdentifier? left, TagGroupIdentifier? right) => Equals(left, right);
    public static bool operator !=(TagGroupIdentifier? left, TagGroupIdentifier? right) => !Equals(left, right);

    public static TagGroupIdentifier NewIdentifier() => (TagGroupIdentifier)Guid.NewGuid();

    public override string ToString() => Identifier.ToString();
    
    public override bool Equals(object? obj) => obj is TagGroupIdentifier other && Equals(other);
    
    public bool Equals(TagGroupIdentifier other) => Identifier.Equals(other.Identifier);
    
    public override int GetHashCode() => Identifier.GetHashCode();
}