using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public readonly struct LinkIdentifier : IEquatable<LinkIdentifier>
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

    public override bool Equals(object? obj) => obj is LinkIdentifier other && Equals(other);

    public bool Equals(LinkIdentifier other) => Identifier.Equals(other.Identifier);

    public override int GetHashCode() => Identifier.GetHashCode();
}