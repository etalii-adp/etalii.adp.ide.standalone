using System.Diagnostics;

namespace EtAlii.Adp;

[DebuggerDisplay("Diagram@{ToString()}")]
public readonly struct DiagramPosition : IEquatable<DiagramPosition>
{
    public required double X { get; init; }
    public required double Y { get; init; }
    
    public DiagramPosition() { }
    
    // ReSharper disable once UnusedMember.Global
    public DiagramPosition(double x, double y) { X = x; Y = y; }
    
    // public static implicit operator DiagramPosition(Vector2 v) => new() { X = v.X, Y = v.Y };
    // // public static explicit operator Vector2(DiagramPosition p) => new(p.X, p.Y);
    // public static explicit operator DiagramPosition(float[] v) => new() { X = v[0], Y = v[1] };
    // public static explicit operator double[](DiagramPosition p) => [p.X, p.Y];
    
    public override string ToString() => $"{X},{Y}";

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(DiagramPosition? left, DiagramPosition? right) => Equals(left, right);

    public static bool operator !=(DiagramPosition? left, DiagramPosition? right) => !Equals(left, right);

    public override bool Equals(object? obj) => obj is DiagramPosition other && Equals(other);

    public bool Equals(DiagramPosition other) => X.Equals(other.X) && Y.Equals(other.Y);
}