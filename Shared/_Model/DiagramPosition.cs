using System.Diagnostics;
using System.Numerics;

namespace EtAlii.Adp;

[DebuggerDisplay("{ToString()}")]
public struct DiagramPosition
{
    public Vector2 Coordinates { get; set; }

    private DiagramPosition(Vector2 coordinates)
    {
        Coordinates = coordinates;
    }
    
    public static implicit operator DiagramPosition(Vector2 v) => new(v);
    public static explicit operator Vector2(DiagramPosition p) => p.Coordinates;
    
    public override string ToString() => $"Position: {Coordinates.X}, {Coordinates.Y}";
}