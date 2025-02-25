using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Node@{ToString()}")]
public readonly struct NodePosition : IEquatable<NodePosition>
{
    public required double X { get; init; }
    public required double Y { get; init; }
    
    // public static implicit operator NodePosition(Vector2 v) => new() { X = v.X, Y = v.Y };
    // // public static explicit operator Vector2(NodePosition p) => new(p.X, p.Y);
    // public static explicit operator NodePosition(float[] v) => new() { X = v[0], Y = v[1] };
    // public static explicit operator double[](NodePosition p) => [p.X, p.Y];
    
    public override string ToString() => $"Position: {X}, {Y}";

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(NodePosition? left, NodePosition? right) => Equals(left, right);

    public static bool operator !=(NodePosition? left, NodePosition? right) => !Equals(left, right);

    public override bool Equals(object? obj) => obj is NodePosition other && Equals(other);

    public bool Equals(NodePosition other) => X.Equals(other.X) && Y.Equals(other.Y);
}