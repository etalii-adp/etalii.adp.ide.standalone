using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("TagIdentifier@{ToString()}")]
public readonly struct TagIdentifier : IEquatable<TagIdentifier>
{
    public required Guid Identifier { get; init; }
    
    public static explicit operator TagIdentifier(Guid id) => new() { Identifier = id };
    public static explicit operator Guid(TagIdentifier did) => did.Identifier;

    public static bool operator ==(TagIdentifier? di, Guid id) => di?.Identifier == id;
    public static bool operator !=(TagIdentifier? di, Guid id) => di?.Identifier != id;
    public static bool operator ==(Guid id, TagIdentifier? di) => di?.Identifier == id;
    public static bool operator !=(Guid id, TagIdentifier? di) => di?.Identifier != id;
    public static bool operator ==(TagIdentifier? left, TagIdentifier? right) => Equals(left, right);
    public static bool operator !=(TagIdentifier? left, TagIdentifier? right) => !Equals(left, right);

    public static TagIdentifier NewIdentifier() => (TagIdentifier)Guid.NewGuid();

    public override string ToString() => Identifier.ToString();
    
    public override bool Equals(object? obj) => obj is TagIdentifier other && Equals(other);
    
    public bool Equals(TagIdentifier other) => Identifier.Equals(other.Identifier);
    
    public override int GetHashCode() => Identifier.GetHashCode();
}