using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Node@{ToString()}")]
public class NodePosition
{
    public required double X { get; init; }
    public required double Y { get; init; }
    
    public NodePosition() { }
    
    // ReSharper disable once UnusedMember.Global
    public NodePosition(double x, double y) { X = x; Y = y; }
    
    // public static implicit operator NodePosition(Vector2 v) => new() { X = v.X, Y = v.Y };
    // // public static explicit operator Vector2(NodePosition p) => new(p.X, p.Y);
    // public static explicit operator NodePosition(float[] v) => new() { X = v[0], Y = v[1] };
    // public static explicit operator double[](NodePosition p) => [p.X, p.Y];
    
    public override string ToString() => $"Position: {X}, {Y}";

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(NodePosition? left, NodePosition? right) => Equals(left, right);

    public static bool operator !=(NodePosition? left, NodePosition? right) => !Equals(left, right);

    public bool Equals(NodePosition? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((NodePosition)obj);
    }
}