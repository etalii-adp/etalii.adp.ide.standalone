using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public readonly struct NodeIdentifier : IEquatable<NodeIdentifier>
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

    public override bool Equals(object? obj) => obj is NodeIdentifier other && Equals(other);

    public bool Equals(NodeIdentifier other) => Identifier.Equals(other.Identifier);

    public override int GetHashCode() => Identifier.GetHashCode();
}