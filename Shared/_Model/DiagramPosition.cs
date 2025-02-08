using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public class DiagramPosition : IEquatable<DiagramPosition>
{
    [JsonConverter(typeof(Vector2Converter))]
    public required Vector2 Coordinates { get; init; }
    
    public static implicit operator DiagramPosition(Vector2 v) => new() { Coordinates = v };
    public static explicit operator Vector2(DiagramPosition p) => p.Coordinates;

    public static explicit operator DiagramPosition(Point v) => new() { Coordinates = new Vector2((float)v.X, (float)v.Y) };
    public static explicit operator Point(DiagramPosition p) => new (p.Coordinates.X, p.Coordinates.Y);
    
    public override string ToString() => $"Position: {Coordinates.X}, {Coordinates.Y}";

    public override int GetHashCode() => Coordinates.GetHashCode();

    public static bool operator ==(DiagramPosition? left, DiagramPosition? right) => Equals(left, right);

    public static bool operator !=(DiagramPosition? left, DiagramPosition? right) => !Equals(left, right);

    public bool Equals(DiagramPosition? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Coordinates.Equals(other.Coordinates);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((DiagramPosition)obj);
    }
}